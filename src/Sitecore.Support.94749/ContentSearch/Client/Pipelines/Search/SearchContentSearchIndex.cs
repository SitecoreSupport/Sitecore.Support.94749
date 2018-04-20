// Decompiled with JetBrains decompiler
// Type: Sitecore.Support.ContentSearch.Client.Pipelines.Search.SearchContentSearchIndex
// Assembly: Sitecore.Support.94749, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 7FCCFD8A-1A03-4EC7-B45B-03645F7105D3
// Assembly location: C:\Sitecore\Support\505968-Content tree search box\Sitecore.Support.94749.dll

using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Abstractions;
using Sitecore.ContentSearch.Linq.Utilities;
using Sitecore.ContentSearch.SearchTypes;
using Sitecore.ContentSearch.Security;
using Sitecore.ContentSearch.Utilities;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Globalization;
using Sitecore.Pipelines.Search;
using Sitecore.Search;
using Sitecore.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Sitecore.StringExtensions;

namespace Sitecore.Support.ContentSearch.Client.Pipelines.Search
{
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

    private bool IsHidden(Item item)
    {
      Assert.ArgumentNotNull((object)item, nameof(item));
      if (item.Appearance.Hidden)
        return true;
      if (item.Parent != null)
        return this.IsHidden(item.Parent);
      return false;
    }

    public virtual void Process(SearchArgs args)
    {
      Assert.ArgumentNotNull((object)args, nameof(args));
      if (!ContentSearchManager.Locator.GetInstance<IContentSearchConfigurationSettings>().ItemBucketsEnabled())
      {
        args.UseLegacySearchEngine = true;
      }
      else
      {
        Item obj1 = args.Root ?? args.Database.GetRootItem();
        Assert.IsNotNull((object)obj1, "rootItem");
        if (args.Query == null && args.TextQuery == null)
          return;
        ISearchIndex index = ContentSearchManager.GetIndex((IIndexable)new SitecoreIndexableItem(obj1));
        if (this.settings == null)
          this.settings = index.Locator.GetInstance<ISettings>();
        using (IProviderSearchContext searchContext = index.CreateSearchContext(SearchSecurityOptions.EnableSecurityCheck))
        {
          List<SitecoreUISearchResultItem> results = new List<SitecoreUISearchResultItem>();
          try
          {
            IQueryable<SitecoreUISearchResultItem> source = (IQueryable<SitecoreUISearchResultItem>)null;
            if (args.Query is CombinedQuery)
            {
              CombinedQuery query1 = (CombinedQuery)args.Query;
              Expression<Func<SitecoreUISearchResultItem, bool>> expression = PredicateBuilder.True<SitecoreUISearchResultItem>();
              foreach (QueryClause clause in (IEnumerable<QueryClause>)query1.Clauses)
              {
                FieldQuery query2 = clause.Query as FieldQuery;
                string field = searchContext.Index.FieldNameTranslator.GetIndexFieldName(query2.FieldName);
                string value = query2.FieldValue;
                expression = expression.Or<SitecoreUISearchResultItem>((Expression<Func<SitecoreUISearchResultItem, bool>>)(i => i[field] == value));
              }
              source = searchContext.GetQueryable<SitecoreUISearchResultItem>().Where<SitecoreUISearchResultItem>(expression);
            }
            else if (!args.TextQuery.IsNullOrEmpty())
            {
              if (!(args.ContentLanguage == (Language)null) && !args.ContentLanguage.Name.IsNullOrEmpty())
                source = searchContext.GetQueryable<SitecoreUISearchResultItem>().Where<SitecoreUISearchResultItem>((Expression<Func<SitecoreUISearchResultItem, bool>>)(i => i.Name.StartsWith(args.TextQuery) || i.Content.Contains(args.TextQuery) && i.Language.Equals(args.ContentLanguage.Name)));
              else
                source = searchContext.GetQueryable<SitecoreUISearchResultItem>().Where<SitecoreUISearchResultItem>((Expression<Func<SitecoreUISearchResultItem, bool>>)(i => i.Name.StartsWith(args.TextQuery) || i.Content.Contains(args.TextQuery)));
              if (args.Root != null && args.Type != SearchType.ContentEditor)
                source = source.Where<SitecoreUISearchResultItem>((Expression<Func<SitecoreUISearchResultItem, bool>>)(i => i.Paths.Contains(args.Root.ID)));
            }
            Func<SitecoreUISearchResultItem, bool> predicate = (Func<SitecoreUISearchResultItem, bool>)(result => results.Count < args.Limit);
            if (source != null)
            {
              foreach (SitecoreUISearchResultItem searchResultItem1 in source.TakeWhile<SitecoreUISearchResultItem>(predicate))
              {
                SitecoreUISearchResultItem result = searchResultItem1;
                if (!UserOptions.View.ShowHiddenItems)
                {
                  Item obj2 = result.GetItem();
                  if (obj2 != null && this.IsHidden(obj2))
                    continue;
                }
                SitecoreUISearchResultItem searchResultItem2 = results.FirstOrDefault<SitecoreUISearchResultItem>((Func<SitecoreUISearchResultItem, bool>)(r => r.ItemId == result.ItemId));
                if (searchResultItem2 == null)
                  results.Add(result);
                else if (!(args.ContentLanguage == (Language)null) && !args.ContentLanguage.Name.IsNullOrEmpty())
                {
                  if (searchResultItem2.Language != args.ContentLanguage.Name && result.Language == args.ContentLanguage.Name || searchResultItem2.Language == result.Language && searchResultItem2.Uri.Version.Number < result.Uri.Version.Number)
                  {
                    results.Remove(searchResultItem2);
                    results.Add(result);
                  }
                }
                else if (args.Type != SearchType.Classic)
                {
                  if (searchResultItem2.Language == result.Language && searchResultItem2.Uri.Version.Number < result.Uri.Version.Number)
                  {
                    results.Remove(searchResultItem2);
                    results.Add(result);
                  }
                }
                else
                  results.Add(result);
              }
            }
          }
          catch (Exception ex)
          {
            Log.Error("Invalid lucene search query: " + args.TextQuery, ex, (object)this);
            return;
          }
          foreach (SitecoreUISearchResultItem searchResultItem in results)
          {
            string title = searchResultItem.DisplayName ?? searchResultItem.Name;
            object obj2 = searchResultItem.Fields.Find((Predicate<KeyValuePair<string, object>>)(pair => pair.Key == Sitecore.Search.BuiltinFields.Icon)).Value ?? (object)this.settings.DefaultIcon();
            string empty = string.Empty;
            if (searchResultItem.Uri != (ItemUri)null)
              empty = searchResultItem.Uri.ToString();
            args.Result.AddResult(new SearchResult(title, obj2.ToString(), empty));
          }
        }
      }
    }
  }
}
