using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace tb_Ignite64;

public class StartupTransportSelectionForm : Form
{
	private MainForm MyMain;

	private IContainer components;

	private Button TCP_but;

	private Button BOX_but;

	private Button Save_IP_but;

	private TextBox IP_Selection_textBox;

	public string IP_Address
	{
		get
		{
			return IP_Selection_textBox.Text;
		}
		set
		{
			IP_Selection_textBox.Text = value;
		}
	}

	public StartupTransportSelectionForm(MainForm TopForm)
	{
		InitializeComponent();
		MyMain = TopForm;
		LoadDefaultIP();
	}

	private void LoadDefaultIP()
	{
		string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "default_ip.txt");
		if (File.Exists(path))
		{
			try
			{
				string text = File.ReadAllText(path);
				IP_Selection_textBox.Text = text;
			}
			catch (Exception ex)
			{
				MessageBox.Show("Error loading IP Address: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		}
	}

	private void TCP_but_click(object sender, EventArgs e)
	{
		MainForm.tcp_ON = true;
		base.DialogResult = DialogResult.OK;
		Close();
	}

	private void BOX_but_click(object sender, EventArgs e)
	{
		MainForm.tcp_ON = false;
		base.DialogResult = DialogResult.OK;
		Close();
	}

	private void SaveIPToDefault()
	{
		string contents = IP_Selection_textBox.Text;
		string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "default_ip.txt");
		try
		{
			File.WriteAllText(path, contents);
			MessageBox.Show("IP Address saved successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		catch (Exception ex)
		{
			MessageBox.Show("Error saving IP Address: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private void SaveIPButton_Click(object sender, EventArgs e)
	{
		SaveIPToDefault();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			components.Dispose();
		}
		base.Dispose(disposing);
	}

	private void InitializeComponent()
	{
		this.TCP_but = new System.Windows.Forms.Button();
		this.BOX_but = new System.Windows.Forms.Button();
		this.Save_IP_but = new System.Windows.Forms.Button();
		this.IP_Selection_textBox = new System.Windows.Forms.TextBox();
		base.SuspendLayout();
		this.TCP_but.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		this.TCP_but.Location = new System.Drawing.Point(38, 38);
		this.TCP_but.Name = "TCP_but";
		this.TCP_but.Size = new System.Drawing.Size(100, 30);
		this.TCP_but.TabIndex = 0;
		this.TCP_but.Text = "TCP / IP";
		this.TCP_but.UseVisualStyleBackColor = true;
		this.TCP_but.Click += new System.EventHandler(TCP_but_click);
		this.BOX_but.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		this.BOX_but.Location = new System.Drawing.Point(38, 88);
		this.BOX_but.Name = "BOX_but";
		this.BOX_but.Size = new System.Drawing.Size(100, 30);
		this.BOX_but.TabIndex = 1;
		this.BOX_but.Text = "SandroBOX";
		this.BOX_but.UseVisualStyleBackColor = true;
		this.BOX_but.Click += new System.EventHandler(BOX_but_click);
		this.Save_IP_but.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.Save_IP_but.Location = new System.Drawing.Point(245, 88);
		this.Save_IP_but.Name = "Save_IP_but";
		this.Save_IP_but.Size = new System.Drawing.Size(150, 30);
		this.Save_IP_but.TabIndex = 2;
		this.Save_IP_but.Text = "Save IP as Default";
		this.Save_IP_but.UseVisualStyleBackColor = true;
		this.Save_IP_but.Click += new System.EventHandler(SaveIPButton_Click);
		this.IP_Selection_textBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.IP_Selection_textBox.Location = new System.Drawing.Point(198, 42);
		this.IP_Selection_textBox.Name = "IP_Selection_textBox";
		this.IP_Selection_textBox.Size = new System.Drawing.Size(250, 22);
		this.IP_Selection_textBox.TabIndex = 3;
		base.AutoScaleDimensions = new System.Drawing.SizeF(6f, 13f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		this.BackColor = System.Drawing.SystemColors.ActiveCaption;
		base.ClientSize = new System.Drawing.Size(485, 143);
		base.Controls.Add(this.IP_Selection_textBox);
		base.Controls.Add(this.Save_IP_but);
		base.Controls.Add(this.BOX_but);
		base.Controls.Add(this.TCP_but);
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.Name = "StartupTransportSelectionForm";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
		this.Text = "Transport Protocol Selection";
		base.TopMost = true;
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
