//
// Message.cs: Reperesents a Meshwork message
//
// Author:
//   Eric Butler <eric@extremeboredom.net>
//
// (C) 2005 FileFind.net (http://filefind.net)
// 

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Meshwork.Backend.Core.Protocol;
using Meshwork.Common;
using Meshwork.Common.Serialization;

namespace Meshwork.Backend.Core
{
	public class Message
	{
		Network network;
		byte[] data;
		
		private Message (Network network, byte[] data, out string messageFrom)
		{
			if (network == null) {
				throw new ArgumentNullException(nameof(network));
			}
			
			if (data == null) {
				throw new ArgumentNullException(nameof(data));
			}
			
			this.network = network;
			this.data = data;
			
			// Read message header
			
			var offset = 0;
				    
			signatureLength = EndianBitConverter.ToUInt64(data, offset);
			offset += 8;
			
			signature = new byte[signatureLength];
			Buffer.BlockCopy(data, offset, signature, 0, (int)signatureLength);
			offset += (int)signatureLength;

			var fromBuffer = new byte[64];
			Buffer.BlockCopy(data, offset, fromBuffer, 0, fromBuffer.Length);
			from = BitConverter.ToString(fromBuffer).Replace("-", string.Empty);
			messageFrom = from;
			offset += 64;
			
			var toBuffer = new byte[64];
			Buffer.BlockCopy(data, offset, toBuffer, 0, toBuffer.Length);
			to = BitConverter.ToString(toBuffer).Replace("-", string.Empty);
			offset += 64;

			type = (MessageType)data[offset];
			offset += 1;
			
			var idBytes = new byte[16];
			Buffer.BlockCopy(data, offset, idBytes, 0, 16);
			id = new Guid(idBytes).ToString();
			offset += 16;
			
			timestamp = EndianBitConverter.ToUInt64(data, offset);
			offset += 8;

			contentLength = EndianBitConverter.ToInt32(data, offset);
			offset += 4;

			var remainingLength = data.Length - offset;
			if (remainingLength != contentLength) {
				throw new Exception($"Message size mismatch! Content length should be {contentLength}, was {remainingLength}");
			}
			
			// If this message isn't for us, ignore the content.
			if (to == network.Core.MyNodeID || to == Network.BroadcastNodeID) {
				
				var contentBuffer = new byte[contentLength];
				Buffer.BlockCopy(data, offset, contentBuffer, 0, contentLength);		
				
				// Decrypt if needed			
				
				if (TypeIsEncrypted(type)) {
					if (From != network.Core.MyNodeID) {
						if (network.Nodes.ContainsKey(From)) {
							contentBuffer = Encryption.Decrypt(network.Nodes[From].CreateDecryptor(), contentBuffer);
						} else {
							throw new Exception($"Node not found: {From}");
						}
					} else {
						contentBuffer = Encryption.Decrypt(network.Nodes[To].CreateDecryptor(), contentBuffer);
					}
				}
				
				// Verify signature
				
				if (From != network.Core.MyNodeID) {
					if (network.TrustedNodes.ContainsKey(from)) {
						var validSignature = network.TrustedNodes[from].CreateCrypto().VerifyData (contentBuffer, new SHA1CryptoServiceProvider(), signature);
						if (validSignature == false) {
							throw new InvalidSignatureException();
						}
					} else if (TypeIsEncrypted(type)) {
						throw new Exception ("Unable to verify message signature! (Type: " + type + ")");
					}
				} else {
					var validSignature = network.Core.CryptoProvider.VerifyData (contentBuffer, new SHA1CryptoServiceProvider(), signature);
					if (validSignature == false) {
						throw new InvalidSignatureException();
					}
				}			
	
				// Now deserialize content
				content = Json.Deserialize(Encoding.UTF8.GetString(contentBuffer), MessageTypeToType[type]);
			}
		}
		
		public static Message Parse (Network network, byte[] data, out string messageFrom)
		{
			var message = new Message(network, data, out messageFrom);
			return message;
		}

		public Message (Network network, MessageType type)
		{
			if (network == null) {
				throw new ArgumentNullException("network");
			}
			
			from = network.LocalNode.NodeID;
			this.type = type;
			id = network.CreateMessageID();
			timestamp = Common.Utils.GetUnixTimestamp();

			this.network = network;
		}

		// Messages use the order as layed out:

		ulong signatureLength;
		byte[] signature;						// Cryptographic signature of everything below (8 bytes)

		string from;							// SHA512(PublicKey) of sender (128 bytes)
		string to = Network.BroadcastNodeID;	// SHA512(PublicKey) of recepient
		MessageType type; 						// MessageTypes enum (1 byte)
		string id;								// Message ID - Randomly generated (16 bytes over wire)
		ulong timestamp;						// Unix timestamp of message creation time (8 bytes)
		int contentLength;						// Size of content (4 bytes)
		
		object content;							// Message content (represented as a byte[])

		// --

		public string From {
			get {
				return from;
			}
			set {
				if (data != null)
					throw new InvalidOperationException("Message has already been signed");
				
				if (!network.Nodes.ContainsKey(value)) {
					throw new Exception ("The specified node was not found (" + value + ").");
				}
			    from = value;
			}
		}

		public string To {
			get
			{
			    if (to == null)
					return Network.BroadcastNodeID;
			    return to;
			}
			set {
				if (data != null)
					throw new InvalidOperationException("Message has already been signed");
				
				if (value == null) {
					throw new ArgumentNullException("value");
				}

				if ((!network.Nodes.ContainsKey(value)) & value != Network.BroadcastNodeID) {
					throw new Exception ("The specified node was not found (" + value + ").");
				}
			    to = value;
			}
		}
		
		public MessageType Type {
			get {
				return type;
			}
			set {
				if (data != null)
					throw new InvalidOperationException("Message has already been signed");
				
				type = value;
			}
		}

		public string MessageID {
			set {
				if (data != null)
					throw new InvalidOperationException("Message has already been signed");
				
				if (value.Length == 16)
					id = value;
				else
					throw new InvalidOperationException("MessageID must be 16 bytes.");
			}
			get {
				return id;
			}
		}
		
		public ulong Timestamp {
			set {
				if (data != null)
					throw new InvalidOperationException("Message has already been signed");
				
				//TODO: verify that it is a valid unix epoch timestamp
				timestamp = value;
			}
			get {
				return timestamp;
			}
		}

		public object Content {
			get {
				return content;
			}
			set {
				if (data != null)
					throw new InvalidOperationException("Message has already been signed");
				
				content = value;
			}
		}

		// --
		
		public byte[] GetAssembledData()
		{
			if (data != null)
				return data;
			
			var index = 0;
			byte[] buffer;

			var contentBytes = Encoding.UTF8.GetBytes(Json.Serialize(content));
			
			// Sign before encrypting
			signature = network.Core.CryptoProvider.SignData(contentBytes, new SHA1CryptoServiceProvider());
			signatureLength = (ulong)signature.Length;
			
			if (TypeIsEncrypted(type)) {

				if (!network.Nodes.ContainsKey(to)) {
					throw new Exception ("network.Nodes[to] was null!... to was " +  to);
				}
			
				// Encrypt if needed
				contentBytes = Encryption.Encrypt(network.Nodes[to].CreateEncryptor(), contentBytes);
			}
			
			signatureLength = (ulong)signature.Length;
			contentLength = contentBytes.Length;
			
			buffer = new byte[8 + (int)signatureLength + 64 + 64 + 1 + 16 + 8 + 4 + contentBytes.Length];

			AppendULongToBuffer(signatureLength, ref buffer, ref index);	// 8
			AppendBytesToBuffer(signature, ref buffer, ref index);			// ?

			AppendStringToBuffer(from, ref buffer, ref index); 				// 64
			AppendStringToBuffer(to, ref buffer, ref index); 				// 64
			
			AppendByteToBuffer((byte)type, ref buffer, ref index);			// 1
			
			var idBytes = new Guid(id).ToByteArray();
			AppendBytesToBuffer(idBytes, ref buffer, ref index); 			// 16

			AppendULongToBuffer(timestamp, ref buffer, ref index);			// 8
			AppendIntToBuffer(contentLength, ref buffer, ref index);		// 4

			AppendBytesToBuffer(contentBytes, ref buffer, ref index);		// ?

			if (index != buffer.Length)
				throw new Exception ("Array was the wrong size: " + index + " " + buffer.Length);

			return buffer;
		}
		
		private static void AppendIntToBuffer (int data, ref byte[] buffer, ref int index)
		{
			Buffer.BlockCopy(EndianBitConverter.GetBytes(data), 
					0, 
					buffer, 
					index, 
					4);
			index += 4;
		}

		private static void AppendULongToBuffer (ulong data, ref byte[] buffer, ref int index)
		{
			Buffer.BlockCopy(EndianBitConverter.GetBytes(data), 
					0, 
					buffer, 
					index, 
					8);
			index += 8;
		}
		
		private static void AppendStringToBuffer (string data, ref byte[] buffer, ref int index)
		{
			var bytes = Common.Utils.StringToBytes(data);
			Buffer.BlockCopy(bytes, 0, buffer, index, bytes.Length);
			index += bytes.Length;
		}
		
		private static void AppendBytesToBuffer (byte[] data, ref byte[] buffer, ref int index)
		{
			Buffer.BlockCopy(data, 0, buffer, index, data.Length);
			index += data.Length;
		}
		
		private static void AppendByteToBuffer (byte data, ref byte[] buffer, ref int index)
		{
			Buffer.SetByte(buffer, index, data);
			//Buffer.BlockCopy(data, 0, buffer, index, 1);
			index += 1;
		}
		
		// XXX: Move all this somewhere else!
	    public static Dictionary<MessageType, Type> MessageTypeToType = new Dictionary<MessageType, Type>()
	    {
	        { MessageType.Auth, typeof(AuthInfo) },
	        { MessageType.AuthReply, typeof(AuthInfo) },
	        { MessageType.PrivateMessage, typeof(string) },
	        { MessageType.ChatroomMessage, typeof(ChatMessage) },
	        { MessageType.MyInfo, typeof(NodeInfo) },
	        { MessageType.Ready, typeof(string) },
	        { MessageType.JoinChat, typeof(ChatAction) },
	        { MessageType.LeaveChat, typeof(ChatAction) },
	        { MessageType.ConnectionDown, typeof(ConnectionInfo) },
	        { MessageType.Ping, typeof(ulong) },
	        { MessageType.Pong, typeof(ulong) },
	        { MessageType.RequestDirListing, typeof(string) },
	        { MessageType.RespondDirListing, typeof(SharedDirectoryInfo) },
	        { MessageType.Ack, typeof(string) },
	        { MessageType.SearchResult, typeof(SearchResultInfo) },
	        { MessageType.SearchRequest, typeof(SearchRequestInfo) },
	        { MessageType.RequestFile, typeof(RequestFileInfo) },
	        { MessageType.NonCriticalError, typeof(MeshworkError) },
	        { MessageType.CriticalError, typeof(MeshworkError) },
	        { MessageType.RequestInfo, typeof(string) },
	        { MessageType.RequestKey, typeof(string) },
	        { MessageType.MyKey, typeof(KeyInfo) },
	        { MessageType.ChatInvite, typeof(ChatInviteInfo) },
	        // { MessageType.SendFile, typeof(SharedFileInfo) },
	        { MessageType.AddMemo, typeof(MemoInfo) },
	        { MessageType.DeleteMemo, typeof(string) },
	        { MessageType.Hello, typeof(HelloInfo) },
	        { MessageType.NewSessionKey, typeof(byte[]) },
	        { MessageType.FileDetails, typeof(SharedFileListing) },
	        { MessageType.TransportConnect, typeof(string) },
	        //{ MessageType.TransportDisconnect, null },
	        //{ MessageType.TransportData, null },
	        //{ MessageType.TransportErro, null },
	        { MessageType.RequestAvatar, typeof(string) },
	        { MessageType.Avatar, typeof(byte[]) },
	        { MessageType.Test, typeof(string) },
	        { MessageType.RequestFileDetails, typeof(string) }
	    };

		public static bool TypeIsEncrypted (MessageType type)
		{
		    if (Network.InsecureMessageTypes.Contains(type) || Network.LocalOnlyMessageTypes.Contains(type) || Network.UnencryptedMessageTypes.Contains(type)) {
				return false;
			}
		    return true;
		}
	}	

	public enum MessageType : byte
	{
		Auth                    = 0x00,
		AuthReply               = 0x01,
		PrivateMessage          = 0x02,
		ChatroomMessage         = 0x03,
		MyInfo                  = 0x04,
		Ready                   = 0x05,
		JoinChat                = 0x06,
		LeaveChat               = 0x07,
		ConnectionDown          = 0x08,
		Ping                    = 0x09,
		Pong                    = 0x0A,
		RequestDirListing       = 0x0B,
		RespondDirListing       = 0x0C,
		Ack	                    = 0x0D,
		SearchResult            = 0x0E,
		SearchRequest           = 0x0F,
		RequestFile	            = 0x10,
		NonCriticalError        = 0x11,
		CriticalError           = 0x12,
		RequestInfo             = 0x13,
		RequestKey              = 0x14,
		MyKey                   = 0x15,
		ChatInvite              = 0x16,
		//SendFile                = 0x17,
		AddMemo                 = 0x18,
		DeleteMemo              = 0x19,
		Hello                   = 0x1A,
		NewSessionKey           = 0x1B,
		FileDetails             = 0x1C,
		TransportConnect        = 0x1D,
		//TransportDisconnect	    = 0x1E,
		//TransportData           = 0x1F,
		//TransportErro           = 0x20,
		RequestAvatar           = 0x21,
		Avatar                  = 0x22,
		Test                    = 0x23,
		RequestFileDetails      = 0x24
	}
}
