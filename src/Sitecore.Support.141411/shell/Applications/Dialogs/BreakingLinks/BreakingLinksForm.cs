using Sitecore;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Globalization;
using Sitecore.Jobs;
using Sitecore.Links;
using Sitecore.Resources;
using Sitecore.SecurityModel;
using Sitecore.Text;
using Sitecore.Web;
using Sitecore.Web.UI.HtmlControls;
using Sitecore.Web.UI.Pages;
using Sitecore.Web.UI.Sheer;
using Sitecore.Web.UI.WebControls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Web.UI;

namespace Sitecore.Support.Shell.Applications.Dialogs.BreakingLinks
{
  public class BreakingLinksForm : DialogForm
  {
    // Fields
    protected Button BackButton;
    protected Border DeletingItems;
    protected Memo ErrorText;
    protected Border ExecutingPage;
    protected Border FailedPage;
    protected TreeviewEx Link;
    protected Literal LinksBrokenOrRemovedCount;
    protected Border LinksBrokenOrRemovedPage;
    protected Radiobutton RelinkButton;
    protected Radiobutton RemoveButton;
    protected Border SelectActionPage;
    protected Border SelectItemPage;

    // Methods
    private void BuildItemsToBeDeleted()
    {
      Assert.IsNotNull(Context.ContentDatabase, "content database");
      HtmlTextWriter writer = new HtmlTextWriter(new StringWriter());
      ListString str = new ListString(UrlHandle.Get()["list"]);
      foreach (string str2 in str)
      {
        Item item = Context.ContentDatabase.GetItem(str2);
        if (item != null)
        {
          writer.Write("<div>");
          writer.Write("<table class=\"scLinkTable\" cellpadding=\"0\" cellspacing=\"0\"><tr>");
          writer.Write("<td>");
          writer.Write(new ImageBuilder
          {
            Src = item.Appearance.Icon,
            Width = 0x20,
            Height = 0x20,
            Class = "scLinkIcon"
          }.ToString());
          writer.Write("</td>");
          writer.Write("<td>");
          writer.Write("<div class=\"scLinkHeader\">");
          writer.Write(item.DisplayName);
          writer.Write("</div>");
          writer.Write("<div class=\"scLinkDetails\">");
          writer.Write(item.Paths.ContentPath);
          writer.Write("</div>");
          writer.Write("</td>");
          writer.Write("</tr></table>");
          writer.Write("</div>");
        }
      }
      this.DeletingItems.InnerHtml = writer.InnerWriter.ToString();
    }

    protected void CheckStatus()
    {
      string str = Context.ClientPage.ServerProperties["handle"] as string;
      Assert.IsNotNullOrEmpty(str, "raw handle");
      Handle handle = Handle.Parse(str);
      if (!handle.IsLocal)
      {
        Context.ClientPage.ClientResponse.Timer("CheckStatus", 500);
      }
      else
      {
        Job job = JobManager.GetJob(handle);
        if (job.Status.Failed)
        {
          this.ErrorText.Value = StringUtil.StringCollectionToString(job.Status.Messages);
          this.ShowPage("Failed");
        }
        else
        {
          string str2;
          if (job.Status.State == JobState.Running)
          {
            str2 = Translate.Text("Processed {0} items. ", new object[] { job.Status.Processed, job.Status.Total });
          }
          else
          {
            str2 = Translate.Text("Queued.");
          }
          if (job.IsDone)
          {
            SheerResponse.SetDialogValue("yes");
            SheerResponse.CloseWindow();
            UrlHandle.DisposeHandle(UrlHandle.Get());
          }
          else
          {
            SheerResponse.SetInnerHtml("Status", str2);
            SheerResponse.Timer("CheckStatus", 500);
          }
        }
      }
    }

    private int CountReferrers(Item item)
    {
      int referrerCount = Globals.LinkDatabase.GetReferrerCount(item);
      foreach (Item item2 in item.Children)
      {
        referrerCount += this.CountReferrers(item2);
      }
      return referrerCount;
    }

    protected void EditLinks()
    {
      UrlString urlString = ResourceUri.Parse("control:EditLinks").ToUrlString();
      if (WebUtil.GetQueryString("ignoreclones") == "1")
      {
        urlString.Add("ignoreclones", "1");
      }
      UrlHandle handle = new UrlHandle();
      handle["list"] = UrlHandle.Get()["list"];
      handle.Add(urlString);
      SheerResponse.ShowModalDialog(urlString.ToString());
    }

    protected void OnBackButton(object sender, EventArgs args)
    {
      Assert.ArgumentNotNull(sender, "sender");
      Assert.ArgumentNotNull(args, "args");
      this.ShowPage("Action");
    }

    protected override void OnCancel(object sender, EventArgs args)
    {
      Assert.ArgumentNotNull(sender, "sender");
      Assert.ArgumentNotNull(args, "args");
      string str = Context.ClientPage.ServerProperties["handle"] as string;
      if (!string.IsNullOrEmpty(str))
      {
        Log.Audit(this, "The RemoveLinks job was cancelled by the user. The target item will therefore not be deleted.  Some or all of the referring links have already been removed or updated.", new string[0]);
      }
      SheerResponse.SetDialogValue("no");
      UrlHandle.DisposeHandle(UrlHandle.Get());
      base.OnCancel(sender, args);
    }

    protected override void OnLoad(EventArgs e)
    {
      Assert.ArgumentNotNull(e, "e");
      base.OnLoad(e);
      if (this.BackButton != null)
      {
        this.BackButton.OnClick += new EventHandler(this.OnBackButton);
      }
      if (!Context.ClientPage.IsEvent)
      {
        this.BuildItemsToBeDeleted();
      }
    }

    protected override void OnOK(object sender, EventArgs args)
    {
      Assert.ArgumentNotNull(sender, "sender");
      Assert.ArgumentNotNull(args, "args");
      string formValue = WebUtil.GetFormValue("Action");
      switch (formValue)
      {
        case "Remove":
          if (this.SelectActionPage.Visible)
          {
            this.ShowLinksBrokenOrRemovedCount();
            return;
          }
          this.StartRemove();
          return;

        case "Relink":
          if (this.SelectActionPage.Visible)
          {
            this.SelectItem();
            return;
          }
          this.StartRelink();
          return;
      }
      if (formValue != "Break")
      {
        throw new InvalidOperationException(string.Format("Unknown action: '{0}'", formValue));
      }
      if (this.SelectActionPage.Visible)
      {
        this.ShowLinksBrokenOrRemovedCount();
      }
      else
      {
        SheerResponse.SetDialogValue("yes");
        base.OnOK(sender, args);
        UrlHandle.DisposeHandle(UrlHandle.Get());
      }
    }

    private void SelectItem()
    {
      this.ShowPage("Item");
    }

    private void ShowLinksBrokenOrRemovedCount()
    {
      Assert.IsNotNull(Context.ContentDatabase, "content database");
      int num = 0;
      ListString str = new ListString(UrlHandle.Get()["list"]);
      foreach (string str2 in str)
      {
        Item item = Context.ContentDatabase.GetItem(str2);
        Assert.IsNotNull(item, "item");
        num += this.CountReferrers(item);
      }
      string formValue = WebUtil.GetFormValue("Action");
      if (formValue == "Remove")
      {
        this.LinksBrokenOrRemovedCount.Text = string.Format(Translate.Text("If you delete this item, you will permanently remove every link to it. Number of links to this item: {0}"), num);
      }
      else
      {
        if (formValue != "Break")
        {
          throw new InvalidOperationException(string.Format("Invalid action: '{0}'", formValue));
        }
        this.LinksBrokenOrRemovedCount.Text = string.Format(Translate.Text("If you delete this item, you will leave broken links. Number of links to this item: {0}"), num);
      }
      this.ShowPage("LinksBrokenOrRemoved");
    }

    private void ShowPage(string pageID)
    {
      Assert.ArgumentNotNullOrEmpty(pageID, "pageID");
      this.SelectActionPage.Visible = pageID == "Action";
      this.SelectItemPage.Visible = pageID == "Item";
      this.ExecutingPage.Visible = pageID == "Executing";
      this.FailedPage.Visible = pageID == "Failed";
      this.LinksBrokenOrRemovedPage.Visible = pageID == "LinksBrokenOrRemoved";
      this.BackButton.Visible = (pageID != "Action") && (pageID != "Executing");
      base.OK.Visible = pageID != "Executing";
    }

    private void StartRelink()
    {
      string list = UrlHandle.Get()["list"];
      Item selectionItem = this.Link.GetSelectionItem();
      if (selectionItem == null)
      {
        SheerResponse.Alert("Select an item.", new string[0]);
      }
      else
      {
        this.ShowPage("Executing");
        Dictionary<string, object> dictionary = new Dictionary<string, object>();
        if (WebUtil.GetQueryString("ignoreclones") == "1")
        {
          dictionary.Add("ignoreclones", "1");
        }
        JobOptions options = new JobOptions("Relink", "Relink", Client.Site.Name, new Relink(list, selectionItem), "RelinkItems")
        {
          AfterLife = TimeSpan.FromMinutes(1.0),
          ContextUser = Context.User,
          CustomData = dictionary
        };
        Job job = JobManager.Start(options);
        Context.ClientPage.ServerProperties["handle"] = job.Handle.ToString();
        Context.ClientPage.ClientResponse.Timer("CheckStatus", 500);
      }
    }

    private void StartRemove()
    {
      string list = UrlHandle.Get()["list"];
      this.ShowPage("Executing");
      Dictionary<string, object> dictionary = new Dictionary<string, object>();
      if (WebUtil.GetQueryString("ignoreclones") == "1")
      {
        dictionary.Add("ignoreclones", "1");
      }
      dictionary["content_database"] = Context.ContentDatabase;
      JobOptions options = new JobOptions("RemoveLinks", "RemoveLinks", Client.Site.Name, new RemoveLinks(list), "Remove")
      {
        AfterLife = TimeSpan.FromMinutes(1.0),
        ContextUser = Context.User,
        CustomData = dictionary
      };
      Job job = JobManager.Start(options);
      Context.ClientPage.ServerProperties["handle"] = job.Handle.ToString();
      Context.ClientPage.ClientResponse.Timer("CheckStatus", 500);
    }

    // Nested Types
    public class Relink
    {
      // Fields
      private Item item;
      private string list;

      // Methods
      public Relink(string list, Item item)
      {
        Assert.ArgumentNotNullOrEmpty(list, "list");
        Assert.ArgumentNotNull(item, "item");
        this.list = list;
        this.item = item;
      }

      private void RelinkItemLinks(Job job, LinkDatabase linkDatabase, string id)
      {
        Assert.ArgumentNotNull(job, "job");
        Assert.ArgumentNotNull(linkDatabase, "linkDatabase");
        Assert.ArgumentNotNullOrEmpty(id, "id");
        Assert.IsNotNull(Context.ContentDatabase, "content database");
        Item targetItem = Context.ContentDatabase.GetItem(id);
        if (targetItem != null)
        {
          JobStatus status = job.Status;
          status.Processed += 1L;
          bool relinkCloneLinks = true;
          if (((job.Options != null) && (job.Options.CustomData != null)) && (job.Options.CustomData is Dictionary<string, object>))
          {
            Dictionary<string, object> customData = job.Options.CustomData as Dictionary<string, object>;
            if ((customData != null) && customData.ContainsKey("ignoreclones"))
            {
              relinkCloneLinks = (customData["ignoreclones"] as string) != "1";
            }
          }
          this.RelinkItemLinks(linkDatabase, targetItem, relinkCloneLinks);
        }
      }

      protected void RelinkItemLinks(LinkDatabase linkDatabase, Item targetItem, bool relinkCloneLinks)
      {
        Assert.ArgumentNotNull(linkDatabase, "linkDatabase");
        Assert.ArgumentNotNull(targetItem, "targetItem");
        foreach (Item item in targetItem.Children)
        {
          this.RelinkItemLinks(linkDatabase, item, relinkCloneLinks);
        }
        foreach (ItemLink link in linkDatabase.GetReferrers(targetItem))
        {
          if (relinkCloneLinks || ((link.SourceFieldID != FieldIDs.Source) && (link.SourceFieldID != FieldIDs.SourceItem)))
          {
            Item sourceItem = link.GetSourceItem();
            if ((sourceItem != null) && !ID.IsNullOrEmpty(link.SourceFieldID))
            {

              #region Fix141411
              /// Default english version can be a fallback version.
              /// In this case we should edit existing version or otherwise new english version will be created instead of the fallback one.
              if (sourceItem.IsFallback)
              {
                sourceItem = sourceItem.GetFallbackItem();
              } 
              #endregion
              this.RelinkLink(sourceItem, link);
            }
          }
        }
      }

      protected void RelinkItems()
      {
        Job job = Context.Job;
        Assert.IsNotNull(job, "job");
        try
        {
          ListString str = new ListString(this.list);
          LinkDatabase linkDatabase = Globals.LinkDatabase;
          foreach (string str2 in str)
          {
            this.RelinkItemLinks(job, linkDatabase, str2);
          }
        }
        catch (Exception exception)
        {
          job.Status.Failed = true;
          job.Status.Messages.Add(exception.ToString());
        }
        job.Status.State = JobState.Finished;
      }

      private void RelinkLink(Item sourceItem, ItemLink itemLink)
      {
        Assert.ArgumentNotNull(sourceItem, "sourceItem");
        Assert.ArgumentNotNull(itemLink, "itemLink");
        Field field = sourceItem.Fields[itemLink.SourceFieldID];
        using (new SecurityDisabler())
        {
          sourceItem.Editing.BeginEdit();
          CustomField field2 = FieldTypeManager.GetField(field);
          if (field2 != null)
          {
            field2.Relink(itemLink, this.item);
            Log.Audit(this, "Relink: {0}, ReferrerItem: {1}", new string[] { AuditFormatter.FormatItem(this.item), AuditFormatter.FormatItem(sourceItem) });
          }
          sourceItem.Editing.EndEdit();
        }
      }
    }

    public class RemoveLinks
    {
      // Fields
      private readonly string list;

      // Methods
      public RemoveLinks(string list)
      {
        Assert.ArgumentNotNullOrEmpty(list, "list");
        this.list = list;
      }

      protected void Remove()
      {
        Job job = Context.Job;
        Assert.IsNotNull(job, "job");
        try
        {
          ListString str = new ListString(this.list);
          LinkDatabase linkDatabase = Globals.LinkDatabase;
          foreach (string str2 in str)
          {
            this.RemoveItemLinks(job, linkDatabase, str2);
          }
        }
        catch (Exception exception)
        {
          job.Status.Failed = true;
          job.Status.Messages.Add(exception.ToString());
        }
        job.Status.State = JobState.Finished;
      }

      private void RemoveItemLinks(Job job, LinkDatabase linkDatabase, string id)
      {
        Assert.ArgumentNotNull(job, "job");
        Assert.ArgumentNotNull(linkDatabase, "linkDatabase");
        Assert.ArgumentNotNullOrEmpty(id, "id");
        Dictionary<string, object> customData = job.Options.CustomData as Dictionary<string, object>;
        Item targetItem = ((customData == null) ? Context.ContentDatabase : ((Database)customData["content_database"])).GetItem(id);
        if (targetItem != null)
        {
          JobStatus status = job.Status;
          status.Processed += 1L;
          bool removeCloneLinks = true;
          if ((customData != null) && customData.ContainsKey("ignoreclones"))
          {
            removeCloneLinks = (customData["ignoreclones"] as string) != "1";
          }
          this.RemoveItemLinks(linkDatabase, targetItem, removeCloneLinks);
        }
      }

      protected void RemoveItemLinks(LinkDatabase linkDatabase, Item targetItem, bool removeCloneLinks)
      {
        Assert.ArgumentNotNull(linkDatabase, "linkDatabase");
        Assert.ArgumentNotNull(targetItem, "targetItem");
        foreach (Item item in targetItem.Children)
        {
          this.RemoveItemLinks(linkDatabase, item, removeCloneLinks);
        }

        foreach (ItemLink link in linkDatabase.GetReferrers(targetItem))
        {
          if (removeCloneLinks || ((link.SourceFieldID != FieldIDs.Source) && (link.SourceFieldID != FieldIDs.SourceItem)))
          {
            Item sourceItem = link.GetSourceItem();
            if ((sourceItem != null) && !ID.IsNullOrEmpty(link.SourceFieldID))
            {
              #region Fix141411
              /// Default english version can be a fallback version.
              /// In this case we should edit existing version or otherwise new english version will be created instead of the fallback one.
              if (sourceItem.IsFallback)
              {
                sourceItem = sourceItem.GetFallbackItem();
              } 
              #endregion
              RemoveLink(sourceItem, link);
            }
          }
        }
        Log.Audit(this, "Remove link: {0}", new string[] { AuditFormatter.FormatItem(targetItem) });
      }

      private static void RemoveLink(Item version, ItemLink itemLink)
      {
        Assert.ArgumentNotNull(version, "version");
        Assert.ArgumentNotNull(itemLink, "itemLink");
        Field field = version.Fields[itemLink.SourceFieldID];
        CustomField field2 = FieldTypeManager.GetField(field);
        if (field2 != null)
        {
          using (new SecurityDisabler())
          {
            version.Editing.BeginEdit();
            field2.RemoveLink(itemLink);
            version.Editing.EndEdit();
          }
        }
      }
    }
  }
}