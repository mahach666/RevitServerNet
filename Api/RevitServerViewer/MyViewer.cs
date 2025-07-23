// (C) Copyright 2014 by Autodesk, Inc. 
//
// Permission to use, copy, modify, and distribute this software
// in object code form for any purpose and without fee is hereby
// granted, provided that the above copyright notice appears in
// all copies and that both that copyright notice and the limited
// warranty and restricted rights notice below appear in all
// supporting documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS. 
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK,
// INC. DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL
// BE UNINTERRUPTED OR ERROR FREE.
//
// Use, duplication, or disclosure by the U.S. Government is
// subject to restrictions set forth in FAR 52.227-19 (Commercial
// Computer Software - Restricted Rights) and DFAR 252.227-7013(c)
// (1)(ii)(Rights in Technical Data and Computer Software), as
// applicable.
//

using System;
using System.Windows.Forms;
using System.Drawing;
using System.Net;
using System.IO;
using System.Text;
using System.Runtime.Serialization.Json;
using System.Xml;
using System.Reflection; 

namespace RevitServerViewer
{
  public partial class MyViewer : Form
  {
    private string [,] supportedVersions = new string[7,2]
    {
      {"2012", "/RevitServerAdminRESTService/AdminRESTService.svc"},
      {"2013", "/RevitServerAdminRESTService2013/AdminRESTService.svc"},
      {"2014", "/RevitServerAdminRESTService2014/AdminRESTService.svc"}, 
      {"2015", "/RevitServerAdminRESTService2015/AdminRESTService.svc"}, 
      {"2016", "/RevitServerAdminRESTService2016/AdminRESTService.svc"}, 
      {"2017", "/RevitServerAdminRESTService2017/AdminRESTService.svc"},
      {"2018", "/RevitServerAdminRESTService2018/AdminRESTService.svc"},
      {"2019", "/RevitServerAdminRESTService2019/AdminRESTService.svc"},
    }; 

    public MyViewer()
    {
      InitializeComponent();

      trvContent.ImageList = new ImageList();
      trvContent.ImageList.Images.Add(
        Image.FromStream(
          Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "RevitServerViewer.Icons.Server.png"
      )));
      trvContent.ImageList.Images.Add(
        Image.FromStream(
          Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "RevitServerViewer.Icons.Folder.png"
      )));
      trvContent.ImageList.Images.Add(
        Image.FromStream(
          Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "RevitServerViewer.Icons.RevitFile.png"
      )));

      for (int i = 0; i < supportedVersions.GetLength(0); i++)
      {
        cbxVersion.Items.Add(supportedVersions[i, 0]); 
      }
      cbxVersion.SelectedIndex = 0;
    }

    private XmlDictionaryReader GetResponse(
      string info
    )
    {
      // Create request

      WebRequest request = 
        WebRequest.Create(
          "http://" + 
          tbxServerName.Text +
          supportedVersions[cbxVersion.SelectedIndex, 1] + 
          info
        );
      request.Method = "GET";

      // Add the information the request needs

      request.Headers.Add("User-Name", Environment.UserName);
      request.Headers.Add("User-Machine-Name", Environment.MachineName);
      request.Headers.Add("Operation-GUID", Guid.NewGuid().ToString()); 

      // Read the response

      XmlDictionaryReaderQuotas quotas = 
        new XmlDictionaryReaderQuotas();
      XmlDictionaryReader jsonReader =
        JsonReaderWriterFactory.CreateJsonReader(
          request.GetResponse().GetResponseStream(),
          quotas
        );
    
      return jsonReader; 
    }

    private void AddContents(
      TreeNode parentNode, 
      string path
    )
    {
      XmlDictionaryReader reader = GetResponse(path + "/contents");

      // Add the folders

      while (reader.Read())
      {
        if (
          reader.NodeType == XmlNodeType.Element && 
          reader.Name == "Folders"
        )
        {
          while (reader.Read())
          {
            if (
              reader.NodeType == XmlNodeType.EndElement && 
              reader.Name == "Folders"
            )
              break;

            if (
              reader.NodeType == XmlNodeType.Element && 
              reader.Name == "Name"
            )
            {
              reader.Read();
              string content = reader.ReadContentAsString();
              TreeNode node = parentNode.Nodes.Add(content);
              node.ImageIndex = node.SelectedImageIndex = 1;
              AddContents(node, path + "|" + content);  
            }
          }
        }
        else if (
          reader.NodeType == XmlNodeType.Element && 
          reader.Name == "Models"
        )
        {
          while (reader.Read())
          {
            if (
              reader.NodeType == XmlNodeType.EndElement && 
              reader.Name == "Models"
            )
              break;

            if (
              reader.NodeType == XmlNodeType.Element && 
              reader.Name == "Name"
            )
            {
              reader.Read(); 
              TreeNode node = parentNode.Nodes.Add(reader.Value);
              node.ImageIndex = node.SelectedImageIndex = 2;
            }
          }
        }
      }

      // Close the reader 

      reader.Close();
    }

    private string ParseValue(
      string value
    )
    {
      if (value.StartsWith("/Date("))
      {
        value = value.Replace("/Date(", "").Replace(")/", "");
        long l = long.Parse(value);
        
        DateTime dt = 
          new DateTime(
            1970, 1, 1, 0, 0, 0, DateTimeKind.Utc
          ).AddMilliseconds(l);
        
        value = 
          dt.ToLongDateString() + 
          " - " + 
          dt.ToLongTimeString();
      }

      return value;
    }

    private void ShowInfo(
      string info
    )
    {
      ltbInfo.Items.Clear(); 

      try
      {
        XmlDictionaryReader reader = GetResponse(info);

        int indent = 0;
        string padding = "";
        string name = null;
        string value = "";

        // Skip the root

        reader.Read(); 

        // Read the rest of the information

        while (reader.Read())
        {
          if (reader.NodeType == XmlNodeType.Element)
          {
            if (name != null)
              ltbInfo.Items.Add(padding.PadLeft(indent, ' ') +  name);

            name = reader.Name;
            
            indent += 2;
          }
          else if (reader.NodeType == XmlNodeType.Text)
          {
            value += reader.Value; 
          }
          else if (reader.NodeType == XmlNodeType.EndElement)
          {
            if (name != null)
            {
              if (value.Length > 0)
                name += " = " + ParseValue(value);
                
              ltbInfo.Items.Add(padding.PadLeft(indent, ' ') + name);
            }

            value = "";
            name = null;

            indent -= 2;
          }
        }

        reader.Close();
      }
      catch (Exception ex)
      {
        MessageBox.Show(ex.Message, "Failed to retrieve data");
      }
    }

    private void btnConnect_Click(
      object sender, 
      EventArgs e
    )
    {
      // Get root folder /|/

      try
      {
        // If we succeeded then let's clear the tree items

        trvContent.Nodes.Clear();

        // Add the root folder

        TreeNode root = 
          trvContent.Nodes.Add("Server");

        // Get the contents of the root folder

        root.ImageIndex = root.SelectedImageIndex = 0;
        AddContents(root, "/|");  

        // Show the root contents

        root.Expand(); 
      }
      catch (Exception ex)
      {
        MessageBox.Show(ex.Message, "Failed to retrieve data");   
      }
    }

    private string GetPath(
      string path
    )
    {
      // Omit the first one which is the server root

      path = "/" + path.Substring(path.IndexOf('|') + 1);
      
      return path; 
    }

    private void trvContent_AfterSelect(
      object sender, 
      TreeViewEventArgs e
    )
    {
      // ImageIndex: 0 = Server, 1 = Folder, 2 = Model

      if (e.Node.ImageIndex == 0)
      {
        // Show server information

        ShowInfo("/serverProperties"); 
      }
      if (e.Node.ImageIndex == 1)
      {
        // Show folder information

        ShowInfo(GetPath(e.Node.FullPath) + "/DirectoryInfo"); 
      }
      else if (e.Node.ImageIndex == 2)
      {
        // Show model file information

        ShowInfo(GetPath(e.Node.FullPath) + "/history"); 
      }
    }
  }
}
