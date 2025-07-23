using System.Windows.Forms;
using System.Drawing;

namespace RevitServerViewer
{
  partial class MyViewer
  {
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
      if (disposing && (components != null))
      {
        components.Dispose();
      }
      base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
      this.btnConnect = new System.Windows.Forms.Button();
      this.tbxServerName = new System.Windows.Forms.TextBox();
      this.groupBox1 = new System.Windows.Forms.GroupBox();
      this.tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
      this.groupBox2 = new System.Windows.Forms.GroupBox();
      this.splitContainer1 = new System.Windows.Forms.SplitContainer();
      this.trvContent = new System.Windows.Forms.TreeView();
      this.ltbInfo = new System.Windows.Forms.ListBox();
      this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
      this.cbxVersion = new System.Windows.Forms.ComboBox();
      this.groupBox1.SuspendLayout();
      this.tableLayoutPanel2.SuspendLayout();
      this.groupBox2.SuspendLayout();
      this.splitContainer1.Panel1.SuspendLayout();
      this.splitContainer1.Panel2.SuspendLayout();
      this.splitContainer1.SuspendLayout();
      this.tableLayoutPanel1.SuspendLayout();
      this.SuspendLayout();
      // 
      // btnConnect
      // 
      this.btnConnect.Location = new System.Drawing.Point(460, 3);
      this.btnConnect.Name = "btnConnect";
      this.btnConnect.Size = new System.Drawing.Size(64, 23);
      this.btnConnect.TabIndex = 0;
      this.btnConnect.Text = "Connect";
      this.btnConnect.UseVisualStyleBackColor = true;
      this.btnConnect.Click += new System.EventHandler(this.btnConnect_Click);
      // 
      // tbxServerName
      // 
      this.tbxServerName.Dock = System.Windows.Forms.DockStyle.Fill;
      this.tbxServerName.Location = new System.Drawing.Point(5, 5);
      this.tbxServerName.Margin = new System.Windows.Forms.Padding(5);
      this.tbxServerName.Name = "tbxServerName";
      this.tbxServerName.Size = new System.Drawing.Size(377, 20);
      this.tbxServerName.TabIndex = 2;
      this.tbxServerName.Text = "ltwesx8";
      // 
      // groupBox1
      // 
      this.groupBox1.Controls.Add(this.tableLayoutPanel2);
      this.groupBox1.Dock = System.Windows.Forms.DockStyle.Fill;
      this.groupBox1.Location = new System.Drawing.Point(8, 8);
      this.groupBox1.Name = "groupBox1";
      this.groupBox1.Size = new System.Drawing.Size(533, 49);
      this.groupBox1.TabIndex = 3;
      this.groupBox1.TabStop = false;
      this.groupBox1.Text = "Server Name or IP";
      // 
      // tableLayoutPanel2
      // 
      this.tableLayoutPanel2.ColumnCount = 3;
      this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
      this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 70F));
      this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 70F));
      this.tableLayoutPanel2.Controls.Add(this.tbxServerName, 0, 0);
      this.tableLayoutPanel2.Controls.Add(this.btnConnect, 2, 0);
      this.tableLayoutPanel2.Controls.Add(this.cbxVersion, 1, 0);
      this.tableLayoutPanel2.Dock = System.Windows.Forms.DockStyle.Fill;
      this.tableLayoutPanel2.Location = new System.Drawing.Point(3, 16);
      this.tableLayoutPanel2.Name = "tableLayoutPanel2";
      this.tableLayoutPanel2.RowCount = 1;
      this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
      this.tableLayoutPanel2.Size = new System.Drawing.Size(527, 30);
      this.tableLayoutPanel2.TabIndex = 3;
      // 
      // groupBox2
      // 
      this.groupBox2.Controls.Add(this.splitContainer1);
      this.groupBox2.Dock = System.Windows.Forms.DockStyle.Fill;
      this.groupBox2.Location = new System.Drawing.Point(8, 63);
      this.groupBox2.Name = "groupBox2";
      this.groupBox2.Padding = new System.Windows.Forms.Padding(7);
      this.groupBox2.Size = new System.Drawing.Size(533, 247);
      this.groupBox2.TabIndex = 4;
      this.groupBox2.TabStop = false;
      this.groupBox2.Text = "Server Content";
      // 
      // splitContainer1
      // 
      this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
      this.splitContainer1.Location = new System.Drawing.Point(7, 20);
      this.splitContainer1.Name = "splitContainer1";
      // 
      // splitContainer1.Panel1
      // 
      this.splitContainer1.Panel1.Controls.Add(this.trvContent);
      // 
      // splitContainer1.Panel2
      // 
      this.splitContainer1.Panel2.Controls.Add(this.ltbInfo);
      this.splitContainer1.Size = new System.Drawing.Size(519, 220);
      this.splitContainer1.SplitterDistance = 173;
      this.splitContainer1.TabIndex = 1;
      // 
      // trvContent
      // 
      this.trvContent.Dock = System.Windows.Forms.DockStyle.Fill;
      this.trvContent.Location = new System.Drawing.Point(0, 0);
      this.trvContent.Name = "trvContent";
      this.trvContent.PathSeparator = "|";
      this.trvContent.ShowRootLines = false;
      this.trvContent.Size = new System.Drawing.Size(173, 220);
      this.trvContent.TabIndex = 0;
      this.trvContent.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.trvContent_AfterSelect);
      // 
      // ltbInfo
      // 
      this.ltbInfo.Dock = System.Windows.Forms.DockStyle.Fill;
      this.ltbInfo.FormattingEnabled = true;
      this.ltbInfo.IntegralHeight = false;
      this.ltbInfo.Location = new System.Drawing.Point(0, 0);
      this.ltbInfo.Name = "ltbInfo";
      this.ltbInfo.ScrollAlwaysVisible = true;
      this.ltbInfo.Size = new System.Drawing.Size(342, 220);
      this.ltbInfo.TabIndex = 0;
      // 
      // tableLayoutPanel1
      // 
      this.tableLayoutPanel1.ColumnCount = 1;
      this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
      this.tableLayoutPanel1.Controls.Add(this.groupBox1, 0, 0);
      this.tableLayoutPanel1.Controls.Add(this.groupBox2, 0, 1);
      this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
      this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
      this.tableLayoutPanel1.MinimumSize = new System.Drawing.Size(100, 300);
      this.tableLayoutPanel1.Name = "tableLayoutPanel1";
      this.tableLayoutPanel1.Padding = new System.Windows.Forms.Padding(5);
      this.tableLayoutPanel1.RowCount = 2;
      this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 55F));
      this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
      this.tableLayoutPanel1.Size = new System.Drawing.Size(549, 318);
      this.tableLayoutPanel1.TabIndex = 5;
      // 
      // cbxVersion
      // 
      this.cbxVersion.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
      this.cbxVersion.FormattingEnabled = true;
      this.cbxVersion.Location = new System.Drawing.Point(391, 4);
      this.cbxVersion.Margin = new System.Windows.Forms.Padding(4);
      this.cbxVersion.Name = "cbxVersion";
      this.cbxVersion.Size = new System.Drawing.Size(62, 21);
      this.cbxVersion.TabIndex = 3;
      // 
      // MyViewer
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(549, 318);
      this.Controls.Add(this.tableLayoutPanel1);
      this.MinimumSize = new System.Drawing.Size(356, 352);
      this.Name = "MyViewer";
      this.Text = "Revit Server Viewer";
      this.groupBox1.ResumeLayout(false);
      this.tableLayoutPanel2.ResumeLayout(false);
      this.tableLayoutPanel2.PerformLayout();
      this.groupBox2.ResumeLayout(false);
      this.splitContainer1.Panel1.ResumeLayout(false);
      this.splitContainer1.Panel2.ResumeLayout(false);
      this.splitContainer1.ResumeLayout(false);
      this.tableLayoutPanel1.ResumeLayout(false);
      this.ResumeLayout(false);

    }

    #endregion

    private System.Windows.Forms.Button btnConnect;
    private System.Windows.Forms.TextBox tbxServerName;
    private System.Windows.Forms.GroupBox groupBox1;
    private System.Windows.Forms.GroupBox groupBox2;
    private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
    private System.Windows.Forms.TreeView trvContent;
    private System.Windows.Forms.SplitContainer splitContainer1;
    private System.Windows.Forms.ListBox ltbInfo;
    private System.Windows.Forms.TableLayoutPanel tableLayoutPanel2;
    private ComboBox cbxVersion;
  }
}

