﻿namespace TrainEditor2.Views
{
	partial class FormExportPanel
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
			disposable.Dispose();

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
			this.components = new System.ComponentModel.Container();
			this.groupBoxPanelXml = new System.Windows.Forms.GroupBox();
			this.buttonPanelXmlFileNameOpen = new System.Windows.Forms.Button();
			this.textBoxPanelXmlFileName = new System.Windows.Forms.TextBox();
			this.labelPanelXmlFileName = new System.Windows.Forms.Label();
			this.groupBoxPanel2Cfg = new System.Windows.Forms.GroupBox();
			this.buttonPanel2CfgFileNameOpen = new System.Windows.Forms.Button();
			this.textBoxPanel2CfgFileName = new System.Windows.Forms.TextBox();
			this.labelPanel2CfgFileName = new System.Windows.Forms.Label();
			this.labelType = new System.Windows.Forms.Label();
			this.comboBoxType = new System.Windows.Forms.ComboBox();
			this.labelTrainIndex = new System.Windows.Forms.Label();
			this.comboBoxTrainIndex = new System.Windows.Forms.ComboBox();
			this.buttonOK = new System.Windows.Forms.Button();
			this.errorProvider = new System.Windows.Forms.ErrorProvider(this.components);
			this.groupBoxPanelXml.SuspendLayout();
			this.groupBoxPanel2Cfg.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.errorProvider)).BeginInit();
			this.SuspendLayout();
			// 
			// groupBoxPanelXml
			// 
			this.groupBoxPanelXml.AllowDrop = true;
			this.groupBoxPanelXml.Controls.Add(this.buttonPanelXmlFileNameOpen);
			this.groupBoxPanelXml.Controls.Add(this.textBoxPanelXmlFileName);
			this.groupBoxPanelXml.Controls.Add(this.labelPanelXmlFileName);
			this.groupBoxPanelXml.Location = new System.Drawing.Point(8, 112);
			this.groupBoxPanelXml.Name = "groupBoxPanelXml";
			this.groupBoxPanelXml.Size = new System.Drawing.Size(208, 72);
			this.groupBoxPanelXml.TabIndex = 49;
			this.groupBoxPanelXml.TabStop = false;
			this.groupBoxPanelXml.Text = "Panel.xml";
			// 
			// buttonPanelXmlFileNameOpen
			// 
			this.buttonPanelXmlFileNameOpen.Location = new System.Drawing.Point(136, 40);
			this.buttonPanelXmlFileNameOpen.Name = "buttonPanelXmlFileNameOpen";
			this.buttonPanelXmlFileNameOpen.Size = new System.Drawing.Size(56, 24);
			this.buttonPanelXmlFileNameOpen.TabIndex = 38;
			this.buttonPanelXmlFileNameOpen.Text = "Open...";
			this.buttonPanelXmlFileNameOpen.UseVisualStyleBackColor = true;
			// 
			// textBoxPanelXmlFileName
			// 
			this.textBoxPanelXmlFileName.Location = new System.Drawing.Point(72, 16);
			this.textBoxPanelXmlFileName.Name = "textBoxPanelXmlFileName";
			this.textBoxPanelXmlFileName.Size = new System.Drawing.Size(120, 19);
			this.textBoxPanelXmlFileName.TabIndex = 37;
			// 
			// labelPanelXmlFileName
			// 
			this.labelPanelXmlFileName.Location = new System.Drawing.Point(8, 16);
			this.labelPanelXmlFileName.Name = "labelPanelXmlFileName";
			this.labelPanelXmlFileName.Size = new System.Drawing.Size(56, 16);
			this.labelPanelXmlFileName.TabIndex = 39;
			this.labelPanelXmlFileName.Text = "Filename:";
			this.labelPanelXmlFileName.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// groupBoxPanel2Cfg
			// 
			this.groupBoxPanel2Cfg.AllowDrop = true;
			this.groupBoxPanel2Cfg.Controls.Add(this.buttonPanel2CfgFileNameOpen);
			this.groupBoxPanel2Cfg.Controls.Add(this.textBoxPanel2CfgFileName);
			this.groupBoxPanel2Cfg.Controls.Add(this.labelPanel2CfgFileName);
			this.groupBoxPanel2Cfg.Location = new System.Drawing.Point(8, 32);
			this.groupBoxPanel2Cfg.Name = "groupBoxPanel2Cfg";
			this.groupBoxPanel2Cfg.Size = new System.Drawing.Size(208, 72);
			this.groupBoxPanel2Cfg.TabIndex = 48;
			this.groupBoxPanel2Cfg.TabStop = false;
			this.groupBoxPanel2Cfg.Text = "Panel2.cfg";
			// 
			// buttonPanel2CfgFileNameOpen
			// 
			this.buttonPanel2CfgFileNameOpen.Location = new System.Drawing.Point(136, 40);
			this.buttonPanel2CfgFileNameOpen.Name = "buttonPanel2CfgFileNameOpen";
			this.buttonPanel2CfgFileNameOpen.Size = new System.Drawing.Size(56, 24);
			this.buttonPanel2CfgFileNameOpen.TabIndex = 38;
			this.buttonPanel2CfgFileNameOpen.Text = "Open...";
			this.buttonPanel2CfgFileNameOpen.UseVisualStyleBackColor = true;
			// 
			// textBoxPanel2CfgFileName
			// 
			this.textBoxPanel2CfgFileName.Location = new System.Drawing.Point(72, 16);
			this.textBoxPanel2CfgFileName.Name = "textBoxPanel2CfgFileName";
			this.textBoxPanel2CfgFileName.Size = new System.Drawing.Size(120, 19);
			this.textBoxPanel2CfgFileName.TabIndex = 37;
			// 
			// labelPanel2CfgFileName
			// 
			this.labelPanel2CfgFileName.Location = new System.Drawing.Point(8, 16);
			this.labelPanel2CfgFileName.Name = "labelPanel2CfgFileName";
			this.labelPanel2CfgFileName.Size = new System.Drawing.Size(56, 16);
			this.labelPanel2CfgFileName.TabIndex = 39;
			this.labelPanel2CfgFileName.Text = "Filename:";
			this.labelPanel2CfgFileName.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// labelType
			// 
			this.labelType.Location = new System.Drawing.Point(8, 8);
			this.labelType.Name = "labelType";
			this.labelType.Size = new System.Drawing.Size(64, 16);
			this.labelType.TabIndex = 46;
			this.labelType.Text = "Type:";
			this.labelType.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// comboBoxType
			// 
			this.comboBoxType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.comboBoxType.FormattingEnabled = true;
			this.comboBoxType.Items.AddRange(new object[] {
            "Panel2.cfg",
            "Panel.xml"});
			this.comboBoxType.Location = new System.Drawing.Point(80, 8);
			this.comboBoxType.Name = "comboBoxType";
			this.comboBoxType.Size = new System.Drawing.Size(120, 20);
			this.comboBoxType.TabIndex = 47;
			// 
			// labelTrainIndex
			// 
			this.labelTrainIndex.Location = new System.Drawing.Point(8, 192);
			this.labelTrainIndex.Name = "labelTrainIndex";
			this.labelTrainIndex.Size = new System.Drawing.Size(64, 16);
			this.labelTrainIndex.TabIndex = 50;
			this.labelTrainIndex.Text = "Train index:";
			this.labelTrainIndex.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// comboBoxTrainIndex
			// 
			this.comboBoxTrainIndex.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.comboBoxTrainIndex.FormattingEnabled = true;
			this.comboBoxTrainIndex.Location = new System.Drawing.Point(80, 192);
			this.comboBoxTrainIndex.Name = "comboBoxTrainIndex";
			this.comboBoxTrainIndex.Size = new System.Drawing.Size(120, 20);
			this.comboBoxTrainIndex.TabIndex = 51;
			// 
			// buttonOK
			// 
			this.buttonOK.Location = new System.Drawing.Point(160, 224);
			this.buttonOK.Name = "buttonOK";
			this.buttonOK.Size = new System.Drawing.Size(56, 24);
			this.buttonOK.TabIndex = 52;
			this.buttonOK.Text = "OK";
			this.buttonOK.UseVisualStyleBackColor = true;
			this.buttonOK.Click += new System.EventHandler(this.ButtonOK_Click);
			// 
			// errorProvider
			// 
			this.errorProvider.ContainerControl = this;
			// 
			// FormExportPanel
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(225, 257);
			this.Controls.Add(this.buttonOK);
			this.Controls.Add(this.comboBoxTrainIndex);
			this.Controls.Add(this.labelTrainIndex);
			this.Controls.Add(this.groupBoxPanelXml);
			this.Controls.Add(this.groupBoxPanel2Cfg);
			this.Controls.Add(this.labelType);
			this.Controls.Add(this.comboBoxType);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
			this.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
			this.Name = "FormExportPanel";
			this.Text = "Export panel file";
			this.Load += new System.EventHandler(this.FormExportPanel_Load);
			this.groupBoxPanelXml.ResumeLayout(false);
			this.groupBoxPanelXml.PerformLayout();
			this.groupBoxPanel2Cfg.ResumeLayout(false);
			this.groupBoxPanel2Cfg.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.errorProvider)).EndInit();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.GroupBox groupBoxPanelXml;
		private System.Windows.Forms.Button buttonPanelXmlFileNameOpen;
		private System.Windows.Forms.TextBox textBoxPanelXmlFileName;
		private System.Windows.Forms.Label labelPanelXmlFileName;
		private System.Windows.Forms.GroupBox groupBoxPanel2Cfg;
		private System.Windows.Forms.Button buttonPanel2CfgFileNameOpen;
		private System.Windows.Forms.TextBox textBoxPanel2CfgFileName;
		private System.Windows.Forms.Label labelPanel2CfgFileName;
		private System.Windows.Forms.Label labelType;
		private System.Windows.Forms.ComboBox comboBoxType;
		private System.Windows.Forms.Label labelTrainIndex;
		private System.Windows.Forms.ComboBox comboBoxTrainIndex;
		private System.Windows.Forms.Button buttonOK;
		private System.Windows.Forms.ErrorProvider errorProvider;
	}
}
