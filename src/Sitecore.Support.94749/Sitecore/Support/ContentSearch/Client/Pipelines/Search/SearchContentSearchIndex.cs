// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SearchContentSearchIndex.cs" company="Sitecore A/S">
//   Copyright (C) 2013 by Sitecore A/S
// </copyright>
// <summary>
//   SearchContentSearchIndex class.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Sitecore.Support.ContentSearch.Client.Pipelines.Search
{
  using System.Collections.Generic;
  using System.Linq;
  using Sitecore.ContentSearch.Abstractions;
  using Sitecore.ContentSearch.Diagnostics;
  using Sitecore.ContentSearch.Exceptions;
  using Sitecore.ContentSearch.SearchTypes;
  using Sitecore.ContentSearch.Utilities;
  using Sitecore.Data.Items;
  using Sitecore.Diagnostics;
  using Sitecore.Pipelines.Search;
  using Sitecore.Search;
  using Sitecore.Shell;
  using Sitecore.StringExtensions;
  using Sitecore.ContentSearch.Maintenance;
  using Sitecore.Events;
  using Sitecore.ContentSearch;
  using Sitecore.ContentSearch.Client.Pipelines.Search;
  using System.Linq.Expressions;
  using Sitecore.ContentSearch.Linq.Utilities;
  using System;
  using Lucene.Net.Search;
  using Lucene.Net.QueryParsers;

  /// <summary>
  /// Searches the system index.
  /// </summary>
  public class SearchContentSearchIndex
  {
    private ISettings settings;

    public SearchContentSearchIndex()
    {


    }

    internal SearchContentSearchIndex(ISettings settings)
    {
      this.settings = settings;
    }

    #region Public methods

    /// <summary>
    /// Runs the processor.
    /// </summary>
    /// <param name="args">The arguments.</param>
    public virtual void Process([NotNull] SearchArgs args)
    {
      Assert.ArgumentNotNull(args, "args");

      if (args.UseLegacySearchEngine)
      {
        return;
      }

      if (!ContentSearchManager.Locator.GetInstance<IContentSearchConfigurationSettings>().ContentSearchEnabled())
      {
        args.UseLegacySearchEngine = true;
        return;
      }

      if (!ContentSearchManager.Locator.GetInstance<ISearchIndexSwitchTracker>().IsOn)
      {
        args.IsIndexProviderOn = false;
        return;
      }

      var rootItem = args.Root ?? args.Database.GetRootItem();
      Assert.IsNotNull(rootItem, "rootItem");

      //Assert.IsNotNull(SearchManager.SystemIndex, "System index");

      if (args.TextQuery.IsNullOrEmpty() && args.Query == null)
      {
        return;
      }

      ISearchIndex index;

      try
      {
        index = ContentSearchManager.GetIndex(new SitecoreIndexableItem(rootItem));
      }
      catch (IndexNotFoundException)
      {
        SearchLog.Log.Warn("No index found for " + rootItem.ID);
        return;
      }

      if (!ContentSearchManager.Locator.GetInstance<ISearchIndexSwitchTracker>().IsIndexOn(index.Name))
      {
        args.IsIndexProviderOn = false;
        return;
      }

      if (this.settings == null)
      {
        this.settings = index.Locator.GetInstance<ISettings>();
      }

      using (var context = index.CreateSearchContext())
      {
        List<SitecoreUISearchResultItem> results = new List<SitecoreUISearchResultItem>();

        try
        {
          IQueryable<SitecoreUISearchResultItem> query = null;
          ///Fix 94749***************         
          if (args.Query is CombinedQuery)
          {
            query = RunCombinedQuery(args, context);
          }
          ///*************
          else
          {
            if (args.Type != SearchType.ContentEditor)
            {
              query = new GenericSearchIndex().Search(args, context);
            }

            if (query == null || Enumerable.Count(query) == 0)
            {
              if (args.ContentLanguage != null && !args.ContentLanguage.Name.IsNullOrEmpty())
              {
                query = context.GetQueryable<SitecoreUISearchResultItem>().Where(i => i.Name.StartsWith(args.TextQuery) || (i.Content.Contains(args.TextQuery) && i.Language.Equals(args.ContentLanguage.Name)));
              }
              else
              {
                query = context.GetQueryable<SitecoreUISearchResultItem>().Where(i => i.Name.StartsWith(args.TextQuery) || i.Content.Contains(args.TextQuery));
              }
            }

            // In content editor, we search the entire tree even if the root is supplied. If it is, the results will get special categorization treatment later on in the pipeline.
            if (args.Root != null && args.Type != SearchType.ContentEditor)
            {
              query = query.Where(i => i.Paths.Contains(args.Root.ID));
            }
          }
          foreach (var result in Enumerable.TakeWhile(query, result => results.Count < args.Limit))
          {
            if (!UserOptions.View.ShowHiddenItems)
            {
              var item = result.GetItem();
              if (item != null && this.IsHidden(item))
              {
                continue;
              }
            }

            var resultForSameItem = results.FirstOrDefault(r => r.ItemId == result.ItemId);
            if (resultForSameItem == null)
            {
              results.Add(result);
              continue;
            }

            if (args.ContentLanguage != null && !args.ContentLanguage.Name.IsNullOrEmpty())
            {
              if ((resultForSameItem.Language != args.ContentLanguage.Name && result.Language == args.ContentLanguage.Name)
                || (resultForSameItem.Language == result.Language && resultForSameItem.Uri.Version.Number < result.Uri.Version.Number))
              {
                results.Remove(resultForSameItem);
                results.Add(result);
              }
            }
            else if (args.Type != SearchType.Classic)
            {
              if (resultForSameItem.Language == result.Language && resultForSameItem.Uri.Version.Number < result.Uri.Version.Number)
              {
                results.Remove(resultForSameItem);
                results.Add(result);
              }
            }
            else
            {
              results.Add(result);
            }
          }
        }
        catch (System.Exception e)
        {
          Log.Error("Invalid lucene search query: " + args.TextQuery, e, this);
          return;
        }

        foreach (var result in results)
        {
          var title = result.DisplayName ?? result.Name;
          object icon = result.Fields.Find(pair => pair.Key == Sitecore.Search.BuiltinFields.Icon).Value
                      ?? result.GetItem().Appearance.Icon ?? this.settings.DefaultIcon();

          string url = string.Empty;
          if (result.Uri != null)
          {
            url = result.Uri.ToString();
          }

          args.Result.AddResult(new SearchResult(title, icon.ToString(), url));
        }
      }
    }

    #endregion

    #region Private methods

    /// <summary>
    /// Determines whether the specified item is hidden.
    /// </summary>
    /// <param name="item">
    /// The item.
    /// </param>
    /// <returns>
    /// <c>true</c> if the specified item is hidden; otherwise, <c>false</c>.
    /// </returns>
    private bool IsHidden([NotNull] Item item)
    {
      Assert.ArgumentNotNull(item, "item");

      return item.Appearance.Hidden || (item.Parent != null && this.IsHidden(item.Parent));
    }

    #endregion

    public IQueryable<SitecoreUISearchResultItem> RunCombinedQuery(SearchArgs args, IProviderSearchContext context)
    {
      Expression<Func<SitecoreUISearchResultItem, bool>> expresion = PredicateBuilder.True<SitecoreUISearchResultItem>();
      using (IEnumerator<QueryClause> enumerator = ((CombinedQuery)args.Query).Clauses.GetEnumerator())
      {
        while (enumerator.MoveNext())
        {
          FieldQuery q = enumerator.Current.Query as FieldQuery;
          string field = context.Index.FieldNameTranslator.GetIndexFieldName(q.FieldName);
          string value = q.FieldValue;
          expresion = expresion.And(i => i[(ObjectIndexerKey)field] == value);
        }
        if (args.ContentLanguage != null && !args.ContentLanguage.Name.IsNullOrEmpty())
        {
          expresion = expresion.And(i => i.Language.Equals(args.ContentLanguage.Name));
        }
        return context.GetQueryable<SitecoreUISearchResultItem>().Where(expresion);
      }
    }
  }
}