using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace tb_Ignite64;

public class MultiTestSelForm : Form
{
	private IContainer components;

	private Button OK_but;

	private Label DCO_Test_Cycles_label;

	private Label DCO_Test_Type_label;

	private Label DCO_Test_MAT_label;

	private Button Cancel_but;

	private Label DCO_Test_DCO_label;

	private Label DCO_Test_Delay_label;

	private NumericUpDown DCO_Test_Delay_numUpDown;

	private Label DCO_Test_Quad_label;

	private Label DCO_Test_PIX_label;

	private CheckBox DCO_Test_AllPIX_chkBox;

	private NumericUpDown DCO_Test_Adj_numUpDown;

	private NumericUpDown DCO_Test_Ctrl_numUpDown;

	private Label DCO_Test_Adj_label;

	private Label DCO_Test_Ctrl_label;

	private GroupBox DCO_Test_Single_groupBox;

	private Label DCO_Test_CAL_TIME_label;

	private Label DCO_Test_PIX_min_label;

	private Label DCO_Test_PIX_MAX_label;

	public ComboBox DCO_Test_Quad_comboBox;

	public NumericUpDown DCO_Test_Cycles_numUpDown;

	public ComboBox DCO_Test_Type_comboBox;

	public ComboBox DCO_Test_MAT_comboBox;

	public NumericUpDown DCO_Test_PIX_MAX_numUpDown;

	public ComboBox DCO_Test_DCO_comboBox;

	public NumericUpDown DCO_Test_CAL_TIME_numUpDown;

	public CheckBox DCO_Test_EnDE_chkBox;

	public NumericUpDown DCO_Test_PIX_min_numUpDown;

	public CheckBox DCO_Test_CrossCouples_chkBox;

	public int Quadrant { get; set; }

	public int Mattonella { get; set; }

	public bool PIXall { get; set; }

	public int PIXmax { get; set; }

	public int PIXmin { get; set; }

	public int TestType { get; set; }

	public int Cycles { get; set; }

	public int DCOSel { get; set; }

	public int Delay { get; set; }

	public int CalibrationTime { get; set; }

	public bool DoubleEdge { get; set; }

	public int SingleAdj { get; set; }

	public int SingleCtrl { get; set; }

	public bool CrossCouples { get; set; }

	public MultiTestSelForm(string TipoDiTest)
	{
		InitializeComponent();
		switch (TipoDiTest)
		{
		case "TestTDC":
			Text = "TDC test settings";
			DCO_Test_PIX_label.Text = "PIX Couple Sel.";
			DCO_Test_PIX_min_label.Text = "Pix1";
			DCO_Test_PIX_MAX_label.Text = "Pix2";
			DCO_Test_Type_comboBox.Items.Clear();
			DCO_Test_Type_comboBox.Items.Add("Scan TA");
			DCO_Test_Type_comboBox.Items.Add("Scan TOT");
			DCO_Test_Type_comboBox.Items.Add("External TA");
			DCO_Test_DCO_comboBox.Items.Clear();
			DCO_Test_DCO_comboBox.Items.Add("Fixed Couples");
			DCO_Test_DCO_comboBox.Items.Add("All Couples");
			DCO_Test_DCO_comboBox.Items.Add("Vertical Couples");
			DCO_Test_DCO_comboBox.Items.Add("Horizontal Couples");
			DCO_Test_DCO_label.Text = "Couple Type";
			DCO_Test_CrossCouples_chkBox.Location = new Point(191, 200);
			DCO_Test_CrossCouples_chkBox.Enabled = true;
			DCO_Test_CrossCouples_chkBox.Visible = true;
			DCO_Test_Delay_label.Visible = false;
			DCO_Test_Delay_numUpDown.Visible = false;
			DCO_Test_Delay_numUpDown.Enabled = false;
			DCO_Test_Adj_numUpDown.Enabled = true;
			DCO_Test_Ctrl_numUpDown.Enabled = true;
			DCO_Test_CAL_TIME_numUpDown.Minimum = 1m;
			DCO_Test_CAL_TIME_numUpDown.Maximum = 10000m;
			DCO_Test_CAL_TIME_numUpDown.Value = 500m;
			DCO_Test_CAL_TIME_numUpDown.Size = new Size(55, 20);
			DCO_Test_CAL_TIME_numUpDown.Enabled = false;
			DCO_Test_CAL_TIME_numUpDown.Visible = true;
			DCO_Test_CAL_TIME_label.Text = "Minimum Data \r\n per Couple";
			DCO_Test_CAL_TIME_label.Visible = true;
			DCO_Test_EnDE_chkBox.Text = "Random TA";
			DCO_Test_Single_groupBox.Enabled = false;
			DCO_Test_Single_groupBox.Visible = false;
			DCO_Test_MAT_comboBox.Items.Remove("MAT BROADCAST");
			break;
		case "TestATP":
			Text = "ATP test settings";
			DCO_Test_PIX_label.Text = "PIX Couple Sel.";
			DCO_Test_PIX_min_label.Text = "Pix1";
			DCO_Test_PIX_MAX_label.Text = "Pix2";
			DCO_Test_Type_comboBox.Visible = false;
			DCO_Test_Type_label.Visible = false;
			DCO_Test_Type_comboBox.Items.Clear();
			DCO_Test_Type_comboBox.Items.Add("Scan TA");
			DCO_Test_Type_comboBox.Items.Add("Scan TOT");
			DCO_Test_DCO_comboBox.Items.Clear();
			DCO_Test_DCO_comboBox.Items.Add("Fixed Couples");
			DCO_Test_DCO_comboBox.Items.Add("All Couples");
			DCO_Test_DCO_comboBox.Items.Add("Vertical Couples");
			DCO_Test_DCO_comboBox.Items.Add("Horizontal Couples");
			DCO_Test_DCO_label.Text = "Couple Type";
			DCO_Test_CrossCouples_chkBox.Location = new Point(191, 200);
			DCO_Test_CrossCouples_chkBox.Enabled = true;
			DCO_Test_CrossCouples_chkBox.Visible = true;
			DCO_Test_Delay_label.Visible = false;
			DCO_Test_Delay_numUpDown.Visible = false;
			DCO_Test_Delay_numUpDown.Enabled = false;
			DCO_Test_Adj_numUpDown.Enabled = true;
			DCO_Test_Ctrl_numUpDown.Enabled = true;
			DCO_Test_CAL_TIME_numUpDown.Enabled = false;
			DCO_Test_CAL_TIME_numUpDown.Visible = false;
			DCO_Test_CAL_TIME_label.Visible = false;
			DCO_Test_EnDE_chkBox.Text = "Random TA";
			DCO_Test_EnDE_chkBox.Visible = false;
			DCO_Test_Single_groupBox.Enabled = false;
			DCO_Test_Single_groupBox.Visible = false;
			DCO_Test_MAT_comboBox.Items.Remove("MAT BROADCAST");
			break;
		case "TestDCO":
			Text = "DCO Test Settings";
			DCO_Test_Type_comboBox.Visible = true;
			DCO_Test_Type_comboBox.Enabled = true;
			DCO_Test_Type_comboBox.Items.Clear();
			DCO_Test_Type_comboBox.Items.Add("Single point");
			DCO_Test_Type_comboBox.Items.Add("Scan");
			DCO_Test_Type_comboBox.Items.Add("Current Configuration");
			DCO_Test_Type_comboBox.SelectedIndex = 0;
			DCO_Test_CrossCouples_chkBox.Enabled = false;
			DCO_Test_CrossCouples_chkBox.Visible = false;
			break;
		case "CalDCO":
			Text = "DCO Calibration Settings";
			DCO_Test_Type_comboBox.Visible = false;
			DCO_Test_Type_comboBox.Enabled = false;
			DCO_Test_Type_label.Visible = false;
			DCO_Test_Type_label.Enabled = false;
			DCO_Test_Cycles_numUpDown.Visible = false;
			DCO_Test_Cycles_numUpDown.Enabled = false;
			DCO_Test_Cycles_label.Visible = false;
			DCO_Test_Cycles_label.Enabled = false;
			DCO_Test_DCO_comboBox.Visible = false;
			DCO_Test_DCO_comboBox.Enabled = false;
			DCO_Test_DCO_label.Visible = false;
			DCO_Test_DCO_label.Enabled = false;
			DCO_Test_CrossCouples_chkBox.Text = "Calibrate MAT 4-7";
			DCO_Test_CrossCouples_chkBox.Location = new Point(191, 120);
			DCO_Test_CrossCouples_chkBox.Enabled = true;
			DCO_Test_CrossCouples_chkBox.Visible = true;
			DCO_Test_Delay_label.Visible = true;
			DCO_Test_Delay_numUpDown.Visible = true;
			DCO_Test_Delay_numUpDown.Enabled = true;
			DCO_Test_Adj_numUpDown.Enabled = true;
			DCO_Test_Ctrl_numUpDown.Enabled = true;
			DCO_Test_Delay_label.Text = "Resolution target (ps)";
			DCO_Test_Delay_numUpDown.Value = 30m;
			DCO_Test_Delay_numUpDown.Minimum = 10m;
			DCO_Test_Delay_numUpDown.Maximum = 100m;
			DCO_Test_Single_groupBox.Text = "DCO-0 Value";
			DCO_Test_Adj_numUpDown.Value = 1m;
			DCO_Test_MAT_comboBox.Items.Remove("MAT BROADCAST");
			break;
		case "ConsAFE":
			Text = "AFE Consumption Test Settings";
			foreach (Control control in base.Controls)
			{
				control.Enabled = false;
				control.Visible = false;
			}
			DCO_Test_Single_groupBox.Enabled = false;
			DCO_Test_Single_groupBox.Visible = false;
			DCO_Test_Quad_comboBox.Enabled = true;
			DCO_Test_Quad_comboBox.Visible = true;
			DCO_Test_Quad_label.Visible = true;
			DCO_Test_MAT_comboBox.Enabled = true;
			DCO_Test_MAT_comboBox.Visible = true;
			DCO_Test_MAT_label.Visible = true;
			DCO_Test_AllPIX_chkBox.Enabled = true;
			DCO_Test_AllPIX_chkBox.Visible = true;
			DCO_Test_PIX_min_numUpDown.Enabled = true;
			DCO_Test_PIX_min_numUpDown.Visible = true;
			DCO_Test_PIX_min_label.Visible = true;
			DCO_Test_PIX_MAX_numUpDown.Enabled = true;
			DCO_Test_PIX_MAX_numUpDown.Visible = true;
			DCO_Test_PIX_MAX_label.Visible = true;
			DCO_Test_Cycles_numUpDown.Minimum = 1m;
			DCO_Test_Cycles_numUpDown.Maximum = 100000m;
			DCO_Test_Cycles_numUpDown.Value = 10000m;
			DCO_Test_Cycles_numUpDown.Visible = true;
			DCO_Test_Cycles_numUpDown.Enabled = true;
			DCO_Test_Cycles_label.Visible = true;
			DCO_Test_MAT_comboBox.Items.Remove("MAT BROADCAST");
			OK_but.Enabled = true;
			OK_but.Visible = true;
			Cancel_but.Enabled = true;
			Cancel_but.Visible = true;
			break;
		}
	}

	private void OK_but_Click(object sender, EventArgs e)
	{
		Quadrant = DCO_Test_Quad_comboBox.SelectedIndex;
		Mattonella = DCO_Test_MAT_comboBox.SelectedIndex;
		PIXall = DCO_Test_AllPIX_chkBox.Checked;
		PIXmax = Convert.ToInt32(DCO_Test_PIX_MAX_numUpDown.Value);
		PIXmin = Convert.ToInt32(DCO_Test_PIX_min_numUpDown.Value);
		TestType = DCO_Test_Type_comboBox.SelectedIndex;
		Cycles = Convert.ToInt32(DCO_Test_Cycles_numUpDown.Value);
		DCOSel = DCO_Test_DCO_comboBox.SelectedIndex;
		Delay = Convert.ToInt32(DCO_Test_Delay_numUpDown.Value);
		CalibrationTime = Convert.ToInt32(DCO_Test_CAL_TIME_numUpDown.Value);
		DoubleEdge = DCO_Test_EnDE_chkBox.Checked;
		SingleAdj = Convert.ToInt32(DCO_Test_Adj_numUpDown.Value);
		SingleCtrl = Convert.ToInt32(DCO_Test_Ctrl_numUpDown.Value);
		CrossCouples = DCO_Test_CrossCouples_chkBox.Checked;
		base.DialogResult = DialogResult.OK;
		Close();
	}

	private void Cancel_but_Click(object sender, EventArgs e)
	{
		base.DialogResult = DialogResult.Cancel;
		Close();
	}

	private void Dynamic_Value_ChangeUI(object sender, EventArgs e)
	{
		if (sender.Equals(DCO_Test_AllPIX_chkBox))
		{
			if (DCO_Test_AllPIX_chkBox.Checked)
			{
				DCO_Test_PIX_min_numUpDown.Value = 0m;
				DCO_Test_PIX_min_numUpDown.Enabled = false;
				DCO_Test_PIX_MAX_numUpDown.Value = 63m;
				DCO_Test_PIX_MAX_numUpDown.Enabled = false;
			}
			else
			{
				DCO_Test_PIX_min_numUpDown.Enabled = true;
				DCO_Test_PIX_MAX_numUpDown.Enabled = true;
			}
		}
		if (Text == "DCO Test Settings" && sender.Equals(DCO_Test_Type_comboBox))
		{
			if (DCO_Test_Type_comboBox.SelectedIndex == 1)
			{
				DCO_Test_Adj_numUpDown.Enabled = false;
				DCO_Test_Ctrl_numUpDown.Enabled = false;
				if (!DCO_Test_MAT_comboBox.Items.Contains("MAT BROADCAST"))
				{
					DCO_Test_MAT_comboBox.Items.Insert(17, "MAT BROADCAST");
				}
			}
			else if (DCO_Test_Type_comboBox.SelectedIndex == 2)
			{
				DCO_Test_Adj_numUpDown.Enabled = false;
				DCO_Test_Ctrl_numUpDown.Enabled = false;
				if (DCO_Test_MAT_comboBox.Items.Contains("MAT BROADCAST"))
				{
					DCO_Test_MAT_comboBox.Items.Remove("MAT BROADCAST");
				}
			}
			else
			{
				DCO_Test_Adj_numUpDown.Enabled = true;
				DCO_Test_Ctrl_numUpDown.Enabled = true;
				if (!DCO_Test_MAT_comboBox.Items.Contains("MAT BROADCAST"))
				{
					DCO_Test_MAT_comboBox.Items.Insert(17, "MAT BROADCAST");
				}
			}
		}
		if (!(Text == "TDC test settings"))
		{
			return;
		}
		if (sender.Equals(DCO_Test_DCO_comboBox))
		{
			if (DCO_Test_DCO_comboBox.SelectedIndex == 1 || DCO_Test_DCO_comboBox.SelectedIndex == 2 || DCO_Test_DCO_comboBox.SelectedIndex == 3)
			{
				DCO_Test_AllPIX_chkBox.Checked = true;
				DCO_Test_PIX_min_numUpDown.Value = 0m;
				DCO_Test_PIX_min_numUpDown.Enabled = false;
				DCO_Test_PIX_MAX_numUpDown.Value = 63m;
				DCO_Test_PIX_MAX_numUpDown.Enabled = false;
			}
			else
			{
				DCO_Test_AllPIX_chkBox.Checked = false;
				DCO_Test_PIX_min_numUpDown.Enabled = true;
				DCO_Test_PIX_MAX_numUpDown.Enabled = true;
			}
		}
		if (sender.Equals(DCO_Test_Type_comboBox))
		{
			if (DCO_Test_Type_comboBox.SelectedIndex == 2)
			{
				DCO_Test_CAL_TIME_numUpDown.Enabled = true;
				DCO_Test_EnDE_chkBox.Enabled = false;
			}
			else
			{
				DCO_Test_CAL_TIME_numUpDown.Enabled = false;
				DCO_Test_EnDE_chkBox.Enabled = true;
			}
		}
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
		this.OK_but = new System.Windows.Forms.Button();
		this.DCO_Test_Cycles_numUpDown = new System.Windows.Forms.NumericUpDown();
		this.DCO_Test_Cycles_label = new System.Windows.Forms.Label();
		this.DCO_Test_Type_comboBox = new System.Windows.Forms.ComboBox();
		this.DCO_Test_Type_label = new System.Windows.Forms.Label();
		this.DCO_Test_MAT_comboBox = new System.Windows.Forms.ComboBox();
		this.DCO_Test_MAT_label = new System.Windows.Forms.Label();
		this.Cancel_but = new System.Windows.Forms.Button();
		this.DCO_Test_Quad_comboBox = new System.Windows.Forms.ComboBox();
		this.DCO_Test_PIX_MAX_numUpDown = new System.Windows.Forms.NumericUpDown();
		this.DCO_Test_DCO_label = new System.Windows.Forms.Label();
		this.DCO_Test_DCO_comboBox = new System.Windows.Forms.ComboBox();
		this.DCO_Test_Delay_label = new System.Windows.Forms.Label();
		this.DCO_Test_Delay_numUpDown = new System.Windows.Forms.NumericUpDown();
		this.DCO_Test_Quad_label = new System.Windows.Forms.Label();
		this.DCO_Test_PIX_label = new System.Windows.Forms.Label();
		this.DCO_Test_AllPIX_chkBox = new System.Windows.Forms.CheckBox();
		this.DCO_Test_Adj_numUpDown = new System.Windows.Forms.NumericUpDown();
		this.DCO_Test_Ctrl_numUpDown = new System.Windows.Forms.NumericUpDown();
		this.DCO_Test_Adj_label = new System.Windows.Forms.Label();
		this.DCO_Test_Ctrl_label = new System.Windows.Forms.Label();
		this.DCO_Test_Single_groupBox = new System.Windows.Forms.GroupBox();
		this.DCO_Test_CAL_TIME_label = new System.Windows.Forms.Label();
		this.DCO_Test_CAL_TIME_numUpDown = new System.Windows.Forms.NumericUpDown();
		this.DCO_Test_EnDE_chkBox = new System.Windows.Forms.CheckBox();
		this.DCO_Test_PIX_min_numUpDown = new System.Windows.Forms.NumericUpDown();
		this.DCO_Test_PIX_min_label = new System.Windows.Forms.Label();
		this.DCO_Test_PIX_MAX_label = new System.Windows.Forms.Label();
		this.DCO_Test_CrossCouples_chkBox = new System.Windows.Forms.CheckBox();
		((System.ComponentModel.ISupportInitialize)this.DCO_Test_Cycles_numUpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DCO_Test_PIX_MAX_numUpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DCO_Test_Delay_numUpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DCO_Test_Adj_numUpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DCO_Test_Ctrl_numUpDown).BeginInit();
		this.DCO_Test_Single_groupBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.DCO_Test_CAL_TIME_numUpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DCO_Test_PIX_min_numUpDown).BeginInit();
		base.SuspendLayout();
		this.OK_but.Location = new System.Drawing.Point(63, 346);
		this.OK_but.Name = "OK_but";
		this.OK_but.Size = new System.Drawing.Size(75, 23);
		this.OK_but.TabIndex = 0;
		this.OK_but.Text = "Start";
		this.OK_but.UseVisualStyleBackColor = true;
		this.OK_but.Click += new System.EventHandler(OK_but_Click);
		this.DCO_Test_Cycles_numUpDown.Location = new System.Drawing.Point(225, 143);
		this.DCO_Test_Cycles_numUpDown.Maximum = new decimal(new int[4] { 999, 0, 0, 0 });
		this.DCO_Test_Cycles_numUpDown.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.DCO_Test_Cycles_numUpDown.Name = "DCO_Test_Cycles_numUpDown";
		this.DCO_Test_Cycles_numUpDown.Size = new System.Drawing.Size(73, 20);
		this.DCO_Test_Cycles_numUpDown.TabIndex = 1;
		this.DCO_Test_Cycles_numUpDown.Value = new decimal(new int[4] { 10, 0, 0, 0 });
		this.DCO_Test_Cycles_label.AutoSize = true;
		this.DCO_Test_Cycles_label.Location = new System.Drawing.Point(37, 145);
		this.DCO_Test_Cycles_label.Name = "DCO_Test_Cycles_label";
		this.DCO_Test_Cycles_label.Size = new System.Drawing.Size(38, 13);
		this.DCO_Test_Cycles_label.TabIndex = 2;
		this.DCO_Test_Cycles_label.Text = "Cycles";
		this.DCO_Test_Type_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.DCO_Test_Type_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.DCO_Test_Type_comboBox.FormattingEnabled = true;
		this.DCO_Test_Type_comboBox.Items.AddRange(new object[2] { "Single point", "Scan DCO Controls" });
		this.DCO_Test_Type_comboBox.Location = new System.Drawing.Point(177, 113);
		this.DCO_Test_Type_comboBox.Name = "DCO_Test_Type_comboBox";
		this.DCO_Test_Type_comboBox.Size = new System.Drawing.Size(121, 21);
		this.DCO_Test_Type_comboBox.TabIndex = 3;
		this.DCO_Test_Type_comboBox.SelectedIndexChanged += new System.EventHandler(Dynamic_Value_ChangeUI);
		this.DCO_Test_Type_label.AutoSize = true;
		this.DCO_Test_Type_label.Location = new System.Drawing.Point(37, 116);
		this.DCO_Test_Type_label.Name = "DCO_Test_Type_label";
		this.DCO_Test_Type_label.Size = new System.Drawing.Size(55, 13);
		this.DCO_Test_Type_label.TabIndex = 4;
		this.DCO_Test_Type_label.Text = "Test Type";
		this.DCO_Test_MAT_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.DCO_Test_MAT_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.DCO_Test_MAT_comboBox.FormattingEnabled = true;
		this.DCO_Test_MAT_comboBox.Items.AddRange(new object[18]
		{
			"MAT 00", "MAT 01", "MAT 02", "MAT 03", "MAT 04 - Disabled", "MAT 05 - Disabled", "MAT 06 - Disabled", "MAT 07 - Disabled", "MAT 08", "MAT 09",
			"MAT 10", "MAT 11", "MAT 12", "MAT 13", "MAT 14", "MAT 15", "MAT ALL", "MAT BROADCAST"
		});
		this.DCO_Test_MAT_comboBox.Location = new System.Drawing.Point(177, 53);
		this.DCO_Test_MAT_comboBox.Name = "DCO_Test_MAT_comboBox";
		this.DCO_Test_MAT_comboBox.Size = new System.Drawing.Size(121, 21);
		this.DCO_Test_MAT_comboBox.TabIndex = 5;
		this.DCO_Test_MAT_label.AutoSize = true;
		this.DCO_Test_MAT_label.Location = new System.Drawing.Point(37, 56);
		this.DCO_Test_MAT_label.Name = "DCO_Test_MAT_label";
		this.DCO_Test_MAT_label.Size = new System.Drawing.Size(75, 13);
		this.DCO_Test_MAT_label.TabIndex = 6;
		this.DCO_Test_MAT_label.Text = "MAT selection";
		this.Cancel_but.Location = new System.Drawing.Point(187, 346);
		this.Cancel_but.Name = "Cancel_but";
		this.Cancel_but.Size = new System.Drawing.Size(75, 23);
		this.Cancel_but.TabIndex = 7;
		this.Cancel_but.Text = "Cancel";
		this.Cancel_but.UseVisualStyleBackColor = true;
		this.Cancel_but.Click += new System.EventHandler(Cancel_but_Click);
		this.DCO_Test_Quad_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.DCO_Test_Quad_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.DCO_Test_Quad_comboBox.FormattingEnabled = true;
		this.DCO_Test_Quad_comboBox.Items.AddRange(new object[6] { "SW (South-West)", "NW (North-West)", "SE (South-East)", "NE (North-East)", "ALL Quadrants", "BROADCAST" });
		this.DCO_Test_Quad_comboBox.Location = new System.Drawing.Point(177, 23);
		this.DCO_Test_Quad_comboBox.Name = "DCO_Test_Quad_comboBox";
		this.DCO_Test_Quad_comboBox.Size = new System.Drawing.Size(121, 21);
		this.DCO_Test_Quad_comboBox.TabIndex = 8;
		this.DCO_Test_PIX_MAX_numUpDown.Location = new System.Drawing.Point(265, 83);
		this.DCO_Test_PIX_MAX_numUpDown.Maximum = new decimal(new int[4] { 63, 0, 0, 0 });
		this.DCO_Test_PIX_MAX_numUpDown.Name = "DCO_Test_PIX_MAX_numUpDown";
		this.DCO_Test_PIX_MAX_numUpDown.Size = new System.Drawing.Size(33, 20);
		this.DCO_Test_PIX_MAX_numUpDown.TabIndex = 9;
		this.DCO_Test_DCO_label.AutoSize = true;
		this.DCO_Test_DCO_label.Location = new System.Drawing.Point(37, 176);
		this.DCO_Test_DCO_label.Name = "DCO_Test_DCO_label";
		this.DCO_Test_DCO_label.Size = new System.Drawing.Size(30, 13);
		this.DCO_Test_DCO_label.TabIndex = 11;
		this.DCO_Test_DCO_label.Text = "DCO";
		this.DCO_Test_DCO_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.DCO_Test_DCO_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.DCO_Test_DCO_comboBox.FormattingEnabled = true;
		this.DCO_Test_DCO_comboBox.Items.AddRange(new object[3] { "DCO 0", "DCO 1", "Both DCOs" });
		this.DCO_Test_DCO_comboBox.Location = new System.Drawing.Point(177, 173);
		this.DCO_Test_DCO_comboBox.Name = "DCO_Test_DCO_comboBox";
		this.DCO_Test_DCO_comboBox.Size = new System.Drawing.Size(121, 21);
		this.DCO_Test_DCO_comboBox.TabIndex = 10;
		this.DCO_Test_DCO_comboBox.SelectedIndexChanged += new System.EventHandler(Dynamic_Value_ChangeUI);
		this.DCO_Test_Delay_label.AutoSize = true;
		this.DCO_Test_Delay_label.Location = new System.Drawing.Point(37, 205);
		this.DCO_Test_Delay_label.Name = "DCO_Test_Delay_label";
		this.DCO_Test_Delay_label.Size = new System.Drawing.Size(34, 13);
		this.DCO_Test_Delay_label.TabIndex = 13;
		this.DCO_Test_Delay_label.Text = "Delay";
		this.DCO_Test_Delay_numUpDown.Location = new System.Drawing.Point(225, 203);
		this.DCO_Test_Delay_numUpDown.Maximum = new decimal(new int[4] { 1000, 0, 0, 0 });
		this.DCO_Test_Delay_numUpDown.Name = "DCO_Test_Delay_numUpDown";
		this.DCO_Test_Delay_numUpDown.RightToLeft = System.Windows.Forms.RightToLeft.No;
		this.DCO_Test_Delay_numUpDown.Size = new System.Drawing.Size(73, 20);
		this.DCO_Test_Delay_numUpDown.TabIndex = 12;
		this.DCO_Test_Quad_label.AutoSize = true;
		this.DCO_Test_Quad_label.Location = new System.Drawing.Point(37, 26);
		this.DCO_Test_Quad_label.Name = "DCO_Test_Quad_label";
		this.DCO_Test_Quad_label.Size = new System.Drawing.Size(78, 13);
		this.DCO_Test_Quad_label.TabIndex = 14;
		this.DCO_Test_Quad_label.Text = "Quad selection";
		this.DCO_Test_PIX_label.AutoSize = true;
		this.DCO_Test_PIX_label.Location = new System.Drawing.Point(37, 85);
		this.DCO_Test_PIX_label.Name = "DCO_Test_PIX_label";
		this.DCO_Test_PIX_label.Size = new System.Drawing.Size(69, 13);
		this.DCO_Test_PIX_label.TabIndex = 15;
		this.DCO_Test_PIX_label.Text = "PIX selection";
		this.DCO_Test_AllPIX_chkBox.AutoSize = true;
		this.DCO_Test_AllPIX_chkBox.Location = new System.Drawing.Point(127, 85);
		this.DCO_Test_AllPIX_chkBox.Name = "DCO_Test_AllPIX_chkBox";
		this.DCO_Test_AllPIX_chkBox.Size = new System.Drawing.Size(43, 17);
		this.DCO_Test_AllPIX_chkBox.TabIndex = 16;
		this.DCO_Test_AllPIX_chkBox.Text = "All?";
		this.DCO_Test_AllPIX_chkBox.UseVisualStyleBackColor = true;
		this.DCO_Test_AllPIX_chkBox.CheckedChanged += new System.EventHandler(Dynamic_Value_ChangeUI);
		this.DCO_Test_Adj_numUpDown.Location = new System.Drawing.Point(33, 18);
		this.DCO_Test_Adj_numUpDown.Maximum = new decimal(new int[4] { 3, 0, 0, 0 });
		this.DCO_Test_Adj_numUpDown.Name = "DCO_Test_Adj_numUpDown";
		this.DCO_Test_Adj_numUpDown.Size = new System.Drawing.Size(35, 20);
		this.DCO_Test_Adj_numUpDown.TabIndex = 17;
		this.DCO_Test_Ctrl_numUpDown.Location = new System.Drawing.Point(99, 18);
		this.DCO_Test_Ctrl_numUpDown.Maximum = new decimal(new int[4] { 15, 0, 0, 0 });
		this.DCO_Test_Ctrl_numUpDown.Name = "DCO_Test_Ctrl_numUpDown";
		this.DCO_Test_Ctrl_numUpDown.Size = new System.Drawing.Size(35, 20);
		this.DCO_Test_Ctrl_numUpDown.TabIndex = 18;
		this.DCO_Test_Adj_label.AutoSize = true;
		this.DCO_Test_Adj_label.Location = new System.Drawing.Point(8, 20);
		this.DCO_Test_Adj_label.Name = "DCO_Test_Adj_label";
		this.DCO_Test_Adj_label.Size = new System.Drawing.Size(22, 13);
		this.DCO_Test_Adj_label.TabIndex = 19;
		this.DCO_Test_Adj_label.Text = "Adj";
		this.DCO_Test_Ctrl_label.AutoSize = true;
		this.DCO_Test_Ctrl_label.Location = new System.Drawing.Point(74, 20);
		this.DCO_Test_Ctrl_label.Name = "DCO_Test_Ctrl_label";
		this.DCO_Test_Ctrl_label.Size = new System.Drawing.Size(22, 13);
		this.DCO_Test_Ctrl_label.TabIndex = 20;
		this.DCO_Test_Ctrl_label.Text = "Ctrl";
		this.DCO_Test_Single_groupBox.BackColor = System.Drawing.Color.PeachPuff;
		this.DCO_Test_Single_groupBox.Controls.Add(this.DCO_Test_Ctrl_label);
		this.DCO_Test_Single_groupBox.Controls.Add(this.DCO_Test_Adj_label);
		this.DCO_Test_Single_groupBox.Controls.Add(this.DCO_Test_Ctrl_numUpDown);
		this.DCO_Test_Single_groupBox.Controls.Add(this.DCO_Test_Adj_numUpDown);
		this.DCO_Test_Single_groupBox.ForeColor = System.Drawing.SystemColors.ControlText;
		this.DCO_Test_Single_groupBox.Location = new System.Drawing.Point(92, 288);
		this.DCO_Test_Single_groupBox.Name = "DCO_Test_Single_groupBox";
		this.DCO_Test_Single_groupBox.Size = new System.Drawing.Size(151, 45);
		this.DCO_Test_Single_groupBox.TabIndex = 21;
		this.DCO_Test_Single_groupBox.TabStop = false;
		this.DCO_Test_Single_groupBox.Text = "Single Point Only";
		this.DCO_Test_CAL_TIME_label.AutoSize = true;
		this.DCO_Test_CAL_TIME_label.Location = new System.Drawing.Point(37, 235);
		this.DCO_Test_CAL_TIME_label.Name = "DCO_Test_CAL_TIME_label";
		this.DCO_Test_CAL_TIME_label.Size = new System.Drawing.Size(82, 13);
		this.DCO_Test_CAL_TIME_label.TabIndex = 24;
		this.DCO_Test_CAL_TIME_label.Text = "Calibration Time";
		this.DCO_Test_CAL_TIME_numUpDown.Location = new System.Drawing.Point(125, 233);
		this.DCO_Test_CAL_TIME_numUpDown.Maximum = new decimal(new int[4] { 3, 0, 0, 0 });
		this.DCO_Test_CAL_TIME_numUpDown.Name = "DCO_Test_CAL_TIME_numUpDown";
		this.DCO_Test_CAL_TIME_numUpDown.Size = new System.Drawing.Size(35, 20);
		this.DCO_Test_CAL_TIME_numUpDown.TabIndex = 23;
		this.DCO_Test_CAL_TIME_numUpDown.Value = new decimal(new int[4] { 3, 0, 0, 0 });
		this.DCO_Test_EnDE_chkBox.AutoSize = true;
		this.DCO_Test_EnDE_chkBox.Location = new System.Drawing.Point(191, 234);
		this.DCO_Test_EnDE_chkBox.Name = "DCO_Test_EnDE_chkBox";
		this.DCO_Test_EnDE_chkBox.Size = new System.Drawing.Size(107, 17);
		this.DCO_Test_EnDE_chkBox.TabIndex = 22;
		this.DCO_Test_EnDE_chkBox.Text = "En. Double Edge";
		this.DCO_Test_EnDE_chkBox.UseVisualStyleBackColor = true;
		this.DCO_Test_PIX_min_numUpDown.Location = new System.Drawing.Point(196, 83);
		this.DCO_Test_PIX_min_numUpDown.Maximum = new decimal(new int[4] { 63, 0, 0, 0 });
		this.DCO_Test_PIX_min_numUpDown.Name = "DCO_Test_PIX_min_numUpDown";
		this.DCO_Test_PIX_min_numUpDown.Size = new System.Drawing.Size(33, 20);
		this.DCO_Test_PIX_min_numUpDown.TabIndex = 25;
		this.DCO_Test_PIX_min_label.AutoSize = true;
		this.DCO_Test_PIX_min_label.Location = new System.Drawing.Point(168, 86);
		this.DCO_Test_PIX_min_label.Name = "DCO_Test_PIX_min_label";
		this.DCO_Test_PIX_min_label.Size = new System.Drawing.Size(29, 13);
		this.DCO_Test_PIX_min_label.TabIndex = 26;
		this.DCO_Test_PIX_min_label.Text = "Start";
		this.DCO_Test_PIX_MAX_label.AutoSize = true;
		this.DCO_Test_PIX_MAX_label.Location = new System.Drawing.Point(237, 86);
		this.DCO_Test_PIX_MAX_label.Name = "DCO_Test_PIX_MAX_label";
		this.DCO_Test_PIX_MAX_label.Size = new System.Drawing.Size(29, 13);
		this.DCO_Test_PIX_MAX_label.TabIndex = 27;
		this.DCO_Test_PIX_MAX_label.Text = "Stop";
		this.DCO_Test_CrossCouples_chkBox.AutoSize = true;
		this.DCO_Test_CrossCouples_chkBox.Location = new System.Drawing.Point(72, 168);
		this.DCO_Test_CrossCouples_chkBox.Name = "DCO_Test_CrossCouples_chkBox";
		this.DCO_Test_CrossCouples_chkBox.Size = new System.Drawing.Size(101, 30);
		this.DCO_Test_CrossCouples_chkBox.TabIndex = 28;
		this.DCO_Test_CrossCouples_chkBox.Text = "Measure across\r\necologic islands";
		this.DCO_Test_CrossCouples_chkBox.UseVisualStyleBackColor = true;
		base.AutoScaleDimensions = new System.Drawing.SizeF(6f, 13f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		base.ClientSize = new System.Drawing.Size(346, 383);
		base.Controls.Add(this.DCO_Test_CrossCouples_chkBox);
		base.Controls.Add(this.DCO_Test_PIX_MAX_numUpDown);
		base.Controls.Add(this.DCO_Test_PIX_MAX_label);
		base.Controls.Add(this.DCO_Test_PIX_min_numUpDown);
		base.Controls.Add(this.DCO_Test_PIX_min_label);
		base.Controls.Add(this.DCO_Test_CAL_TIME_label);
		base.Controls.Add(this.DCO_Test_CAL_TIME_numUpDown);
		base.Controls.Add(this.DCO_Test_EnDE_chkBox);
		base.Controls.Add(this.DCO_Test_Single_groupBox);
		base.Controls.Add(this.DCO_Test_AllPIX_chkBox);
		base.Controls.Add(this.DCO_Test_PIX_label);
		base.Controls.Add(this.DCO_Test_Quad_label);
		base.Controls.Add(this.DCO_Test_Delay_label);
		base.Controls.Add(this.DCO_Test_Delay_numUpDown);
		base.Controls.Add(this.DCO_Test_DCO_label);
		base.Controls.Add(this.DCO_Test_DCO_comboBox);
		base.Controls.Add(this.DCO_Test_Quad_comboBox);
		base.Controls.Add(this.Cancel_but);
		base.Controls.Add(this.DCO_Test_MAT_label);
		base.Controls.Add(this.DCO_Test_MAT_comboBox);
		base.Controls.Add(this.DCO_Test_Type_label);
		base.Controls.Add(this.DCO_Test_Type_comboBox);
		base.Controls.Add(this.DCO_Test_Cycles_label);
		base.Controls.Add(this.DCO_Test_Cycles_numUpDown);
		base.Controls.Add(this.OK_but);
		base.Name = "MultiTestSelForm";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "Test Settings Selection";
		((System.ComponentModel.ISupportInitialize)this.DCO_Test_Cycles_numUpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DCO_Test_PIX_MAX_numUpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DCO_Test_Delay_numUpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DCO_Test_Adj_numUpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DCO_Test_Ctrl_numUpDown).EndInit();
		this.DCO_Test_Single_groupBox.ResumeLayout(false);
		this.DCO_Test_Single_groupBox.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.DCO_Test_CAL_TIME_numUpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DCO_Test_PIX_min_numUpDown).EndInit();
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
