//
// FileSearch.cs:
//
// Authors:
//   Eric Butler <eric@extremeboredom.net>
//
// (C) 2007 FileFind.net (http://filefind.net)
//

using System;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Collections;
using FileFind.Collections;
using FileFind.Meshwork.Protocol;
using FileFind.Meshwork.Filesystem;

namespace FileFind.Meshwork.Search
{
	public delegate void NewResultsEventHandler (FileSearch search, SearchResult[] results);

	public class FileSearch
	{
		string name;
		string query;
		bool filtersEnabled = false;
		List<FileSearchFilter> filters;
		List<string> networkIds;
		[NonSerialized] Dictionary<string,SearchResult> results;
		[NonSerialized] Dictionary<string,List<SearchResult>> allFileResults;

		int id;

		public event NewResultsEventHandler NewResults;
		public event EventHandler ClearedResults;

		public FileSearch ()
		{
			filters = new List<FileSearchFilter>();
			networkIds = new List<string>();

			results = new Dictionary<string, SearchResult>();
			allFileResults = new Dictionary<string, List<SearchResult>>();

			id = new Random().Next();
		}

		public string Name {
			get {
				return name;
			}
			set {
				name = value;
			}
		}

		public string Query {
			get {
				return query;
			}
			set {
				if (value == null) {
					throw new ArgumentNullException();
				} else if (value.Trim() == String.Empty) {
					throw new ArgumentException("Query may not be empty.");
				}

				query = value.ToLower();
			}
		}

		public int Id {
			get {
				return id;
			}
		}

		public bool FiltersEnabled {
			get {
				return filtersEnabled;
			}
			set {
				filtersEnabled = value;
			}
		}

		public List<FileSearchFilter> Filters {
			get {
				return filters;
			}
		}

		public List<string> NetworkIds {
			get {
				return networkIds;
			}
		}
		
		public void Repeat ()
		{
			id = new Random().Next();
			results.Clear();
			allFileResults.Clear();
			
			if (ClearedResults != null)
				ClearedResults(this, EventArgs.Empty);
			
			foreach (Network network in Core.Networks) {
				if (networkIds.Count == 0 || networkIds.IndexOf(network.NetworkID) > -1) { 
					network.FileSearch(this);
				}
			}
		}

		[XmlIgnore]
		public ReadOnlyDictionary<string,SearchResult> Results {
			get {
				return new ReadOnlyDictionary<string,SearchResult>(results);
			}
		}

		[XmlIgnore]
		public ReadOnlyDictionary<string,List<SearchResult>> AllFileResults {
			get {
				// XXX: Can we make the List<SearchResult> readonly too?
				return new ReadOnlyDictionary<string,List<SearchResult>>(allFileResults);
			}
		}

		public void AppendResults (Node node, SearchResultInfo resultInfo)
		{
			List<SearchResult> newResults = new List<SearchResult>();

			if (resultInfo.SearchId != id) {
				throw new ArgumentException("Results are for a different search.");
			}

			foreach (SharedDirectoryInfo dir in resultInfo.Directories) {
				SearchResult directoryResult = new SearchResult(SearchResultType.Directory, node, dir);

				// Create a random key for directories, we never look them up.
				Random random = new Random();
				string key = random.Next().ToString();
				results[key] = directoryResult;

				if (dir.Files != null) {
					foreach (SharedFileListing file in dir.Files) {

						SearchResult fileResult = new SearchResult(SearchResultType.File, node, file);
						directoryResult.Add(fileResult);

						if (!allFileResults.ContainsKey(file.InfoHash)) {
							allFileResults[file.InfoHash] = new List<SearchResult>();
						}
						allFileResults[file.InfoHash].Add(fileResult);
					}
				}
					
				newResults.Add(directoryResult);
			}

			foreach (SharedFileListing file in resultInfo.Files) {

				SearchResult result = new SearchResult(SearchResultType.File, node, file);

				if (!results.ContainsKey(file.InfoHash)) {
					SearchResult resultGroup = new SearchResult(SearchResultType.File, node, file.InfoHash);
					resultGroup.Add(result);

					results[file.InfoHash] = resultGroup;

					newResults.Add(resultGroup);
				} else {
					SearchResult existingResult = results[file.InfoHash];
					existingResult.Add(result);
				}

				if (!allFileResults.ContainsKey(file.InfoHash)) {
					allFileResults[file.InfoHash] = new List<SearchResult>();
				}
				allFileResults[file.InfoHash].Add(result);
			}
			
			if (NewResults != null) {
				NewResults(this, newResults.ToArray());
			}
		}

		public bool CheckAllFiltersMatchesOne (SearchResult result)
		{
			foreach (SearchResult child in result.Children) {
				if (CheckAllFilters((SharedFileListing)child.Listing)) {
					return true;
				}
			}
			return false;
		}

		public bool CheckAllFilters (SharedFileListing listing)
		{
			foreach (FileSearchFilter filter in filters) {
				if (!filter.Check(listing)) {
					return false;
				}
			}
			return true;
		}
	}
}
