using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace tb_Ignite64;

public class PlotResult : Form
{
	private bool Debug = MainForm.debug;

	private string dirResRun;

	private string[] files;

	private bool DCOtest;

	private bool TDCtest;

	private double[,,,] PerDco = new double[16, 2, 64, 1000];

	private double[,,] taTDC = new double[16, 16, 1000];

	private double[,] TdcDcoPer = new double[16, 2];

	private IContainer components;

	private Chart RESchart;

	private MenuStrip menuStrip1;

	private ToolStripMenuItem fileToolStripMenuItem;

	private ToolStripMenuItem exitToolStripMenuItem;

	private ToolStripMenuItem plotsToolStripMenuItem;

	private ToolStripMenuItem dCOToolStripMenuItem;

	private ToolStripMenuItem tDCToolStripMenuItem;

	private ToolStripSeparator toolStripSeparator1;

	private ToolStripMenuItem toolStripMenuItem2;

	private ToolStripMenuItem tDCScanToolStripMenuItem;

	private OpenFileDialog openFileDialog1;

	private ToolStripMenuItem loadRunToolStripMenuItem;

	private ToolStripSeparator toolStripSeparator2;

	private FolderBrowserDialog folderBrowserDialog1;

	private ComboBox PlotSel_comboBox;

	private Label label3;

	private TextBox Cycle_txtBox;

	private Label label4;

	private Label label5;

	private ComboBox TDCsel_comboBox;

	private ComboBox DCOsel_comboBox;

	private Label label6;

	private NumericUpDown CodeSel_numUpDw;

	private GroupBox Sel_groupBox;

	private Label label1;

	private GroupBox Info_groupBox;

	private Label label2;

	private TextBox Date_txtBox;

	private GroupBox groupBox1;

	private Label label7;

	private TextBox Sigma_txtBox;

	private TextBox Max_txtBox;

	private TextBox Min_txtBox;

	private TextBox Avg_txtBox;

	private Label label8;

	private TextBox LineSigma_txtBox;

	private TextBox LineInterc_txtBox;

	private TextBox LineAngCoef_txtBox;

	private Label DCOper_label;

	private TextBox DcoRes_txtBox;

	private TextBox DcoT1_txtBox;

	private TextBox DcoT0_txtBox;

	private GroupBox groupBox2;

	private TextBox PlotX_Min;

	private TextBox PlotX_Max;

	private TextBox PlotX_Step;

	public PlotResult()
	{
		InitializeComponent();
		TDCsel_comboBox.SelectedIndex = 0;
		DCOsel_comboBox.SelectedIndex = 0;
		RESchart.Titles[0].Text = "Plot Results";
		RESchart.Series.Add("Generic");
		RESchart.Series[0].ChartType = SeriesChartType.Point;
		RESchart.ChartAreas[0].Name = "GenericXY";
		RESchart.ChartAreas[0].AxisX.Title = "Time Phase (ps)";
		RESchart.ChartAreas[0].AxisX.Minimum = 0.0;
		RESchart.ChartAreas[0].AxisX.Maximum = 25000.0;
		RESchart.ChartAreas[0].AxisX.Interval = 3125.0;
		RESchart.ChartAreas[0].AxisY.Title = "Time Phase Measured (ps)";
		RESchart.ChartAreas[0].AxisY.Minimum = 0.0;
		RESchart.ChartAreas[0].AxisY.Maximum = 25000.0;
		RESchart.ChartAreas[0].AxisY.Interval = 3125.0;
	}

	private void InitChartResult()
	{
		if (TDCtest)
		{
			RESchart.Titles[0].Text = "TDC test Plot Results";
			RESchart.Series[0].ChartType = SeriesChartType.Point;
			RESchart.ChartAreas[0].Name = "GenericXY";
			RESchart.ChartAreas[0].AxisX.Title = "Time Phase (ps)";
			RESchart.ChartAreas[0].AxisX.Minimum = 0.0;
			RESchart.ChartAreas[0].AxisX.Maximum = 25000.0;
			RESchart.ChartAreas[0].AxisX.Interval = 1562.5;
			RESchart.ChartAreas[0].AxisY.Title = "Time Phase Measured (ps)";
			RESchart.ChartAreas[0].AxisY.Minimum = 0.0;
			RESchart.ChartAreas[0].AxisY.Maximum = 25000.0;
			RESchart.ChartAreas[0].AxisY.Interval = 1562.5;
		}
	}

	private void initVectorData(string vectorDataName)
	{
		if (vectorDataName.Equals("TDC"))
		{
			for (int i = 0; i < 16; i++)
			{
				for (int j = 0; j < 16; j++)
				{
					for (int k = 0; k < 1000; k++)
					{
						taTDC[i, j, k] = -1.0;
					}
				}
			}
		}
		else if (vectorDataName.Equals("DCO"))
		{
			for (int l = 0; l < 16; l++)
			{
				for (int m = 0; m <= 1; m++)
				{
					for (int n = 0; n < 64; n++)
					{
						for (int num = 0; num < 1000; num++)
						{
							PerDco[l, m, n, num] = -1.0;
						}
					}
				}
			}
		}
		else
		{
			if (!vectorDataName.Equals("TdcDco"))
			{
				return;
			}
			for (int num2 = 0; num2 < 16; num2++)
			{
				for (int num3 = 0; num3 <= 1; num3++)
				{
					TdcDcoPer[num2, num3] = -1.0;
				}
			}
		}
	}

	private void buildPlot(string plotType)
	{
		CodeSel_numUpDw.Visible = false;
		Min_txtBox.Text = "";
		Max_txtBox.Text = "";
		Avg_txtBox.Text = "";
		Sigma_txtBox.Text = "";
		LineAngCoef_txtBox.Text = "";
		LineInterc_txtBox.Text = "";
		LineSigma_txtBox.Text = "";
		int num = ((TDCsel_comboBox.SelectedIndex < 16) ? TDCsel_comboBox.SelectedIndex : 0);
		int num2 = ((TDCsel_comboBox.SelectedIndex < 16) ? TDCsel_comboBox.SelectedIndex : 15);
		int num3 = ((DCOsel_comboBox.SelectedIndex < 2) ? DCOsel_comboBox.SelectedIndex : 0);
		int num4 = ((DCOsel_comboBox.SelectedIndex >= 2) ? 1 : DCOsel_comboBox.SelectedIndex);
		if (num == num2)
		{
			DcoT0_txtBox.Text = TdcDcoPer[num, 0].ToString("0.00");
			DcoT1_txtBox.Text = TdcDcoPer[num, 1].ToString("0.00");
			DcoRes_txtBox.Text = (TdcDcoPer[num, 0] - TdcDcoPer[num, 1]).ToString("0.00");
		}
		if (plotType.Equals("TAscan"))
		{
			RESchart.Titles[0].Text = "TDC TA scan";
			RESchart.Series.Clear();
			RESchart.ChartAreas[0].Name = "GenericXY";
			RESchart.ChartAreas[0].AxisX.Title = "Time Phase Expected (ps)";
			RESchart.ChartAreas[0].AxisX.Minimum = (PlotX_Min.Text.Equals("") ? 0.0 : Convert.ToDouble(PlotX_Min.Text));
			RESchart.ChartAreas[0].AxisX.Maximum = (PlotX_Max.Text.Equals("") ? 25000.0 : Convert.ToDouble(PlotX_Max.Text));
			RESchart.ChartAreas[0].AxisX.Interval = 1562.5;
			RESchart.ChartAreas[0].AxisY.Title = "Time Phase Measured (ps)";
			RESchart.ChartAreas[0].AxisY.Minimum = 0.0;
			RESchart.ChartAreas[0].AxisY.Maximum = 25000.0;
			RESchart.ChartAreas[0].AxisY.Interval = 1562.5;
			for (int i = num; i <= num2; i++)
			{
				RESchart.Series.Add(i.ToString());
				RESchart.Series[i - num].ChartType = SeriesChartType.Point;
				RESchart.Legends[0].Enabled = true;
				for (int j = 1; j <= 15; j++)
				{
					for (int k = 0; k < Convert.ToInt32(Cycle_txtBox.Text); k++)
					{
						RESchart.Series[i - num].Points.AddXY(25000.0 - (double)j * 1562.5, taTDC[i, j, k]);
					}
				}
			}
			if (num == num2)
			{
				Scanfitting("TAscan", TDCsel_comboBox.SelectedIndex, out var mLine, out var qLine, out var sigma);
				LineAngCoef_txtBox.Text = mLine.ToString("0.0000");
				LineInterc_txtBox.Text = qLine.ToString("0.00");
				LineSigma_txtBox.Text = sigma.ToString("0.000");
			}
		}
		else if (plotType.Equals("TAscanError"))
		{
			RESchart.Titles[0].Text = "TDC TA scan Error";
			RESchart.Series.Clear();
			RESchart.ChartAreas[0].Name = "GenericXY";
			RESchart.ChartAreas[0].AxisX.Title = "Time Phase Expected (ps)";
			RESchart.ChartAreas[0].AxisX.Minimum = 0.0;
			RESchart.ChartAreas[0].AxisX.Maximum = 25000.0;
			RESchart.ChartAreas[0].AxisX.Interval = 1562.5;
			RESchart.ChartAreas[0].AxisY.Title = "Time Error Measured (ps)";
			RESchart.ChartAreas[0].AxisY.Minimum = double.NaN;
			RESchart.ChartAreas[0].AxisY.Maximum = double.NaN;
			RESchart.ChartAreas[0].AxisY.Interval = 100.0;
			for (int l = num; l <= num2; l++)
			{
				RESchart.Series.Add(l.ToString());
				RESchart.Series[l - num].ChartType = SeriesChartType.Point;
				RESchart.Legends[0].Enabled = true;
				for (int m = 1; m <= 15; m++)
				{
					for (int n = 0; n < Convert.ToInt32(Cycle_txtBox.Text); n++)
					{
						RESchart.Series[l - num].Points.AddXY(25000.0 - (double)m * 1562.5, taTDC[l, m, n] - (25000.0 - (double)m * 1562.5));
					}
				}
			}
			if (num == num2)
			{
				Scanfitting("TAscanError", TDCsel_comboBox.SelectedIndex, out var mLine2, out var qLine2, out var sigma2);
				LineAngCoef_txtBox.Text = mLine2.ToString("0.0000");
				LineInterc_txtBox.Text = qLine2.ToString("0.00");
				LineSigma_txtBox.Text = sigma2.ToString("0.000");
				Avg_txtBox.Text = RESchart.Series[0].Points.Select((DataPoint v) => v.YValues[0]).Average().ToString("0.00");
				Min_txtBox.Text = RESchart.Series[0].Points.Select((DataPoint v) => v.YValues[0]).Min().ToString("0.00");
				Max_txtBox.Text = RESchart.Series[0].Points.Select((DataPoint v) => v.YValues[0]).Max().ToString("0.00");
			}
		}
		else if (plotType.Equals("TAscanErrorHisto"))
		{
			RESchart.Titles[0].Text = "TDC TA scan Error";
			RESchart.Series.Clear();
			RESchart.ChartAreas[0].Name = "GenericXY";
			RESchart.ChartAreas[0].AxisX.Title = "TAmis - TAexp (ps)";
			RESchart.ChartAreas[0].AxisX.Minimum = double.NaN;
			RESchart.ChartAreas[0].AxisX.Maximum = double.NaN;
			RESchart.ChartAreas[0].AxisX.Interval = 50.0;
			RESchart.ChartAreas[0].AxisY.Title = "Counts";
			RESchart.ChartAreas[0].AxisY.Minimum = double.NaN;
			RESchart.ChartAreas[0].AxisY.Maximum = double.NaN;
			RESchart.ChartAreas[0].AxisY.Interval = 100.0;
			for (int num5 = num; num5 <= num2; num5++)
			{
				int[] array = new int[2000];
				int[] array2 = new int[2000];
				RESchart.Series.Add(num5.ToString());
				RESchart.Series[num5 - num].ChartType = SeriesChartType.Column;
				RESchart.Legends[0].Enabled = true;
				for (int num6 = 1; num6 <= 15; num6++)
				{
					for (int num7 = 0; num7 < Convert.ToInt32(Cycle_txtBox.Text); num7++)
					{
						if ((int)(taTDC[num5, num6, num7] - (25000.0 - (double)num6 * 1562.5)) >= 0)
						{
							array[(int)(taTDC[num5, num6, num7] - (25000.0 - (double)num6 * 1562.5))]++;
						}
						else
						{
							array2[-(int)(taTDC[num5, num6, num7] - (25000.0 - (double)num6 * 1562.5))]++;
						}
					}
				}
				for (int num8 = -1999; num8 < 2000; num8++)
				{
					if (num8 < 0)
					{
						RESchart.Series[num5 - num].Points.AddXY(num8, array2[-num8]);
					}
					else
					{
						RESchart.Series[num5 - num].Points.AddXY(num8, array[num8]);
					}
				}
			}
			if (num == num2)
			{
				TAerr_HistFitting(num, out var avg, out var sigma3);
				Avg_txtBox.Text = avg.ToString("0.00");
				Sigma_txtBox.Text = sigma3.ToString("0.00");
				Min_txtBox.Text = RESchart.Series[0].Points.Select((DataPoint v) => v.XValue).Min().ToString("0.00");
				Max_txtBox.Text = RESchart.Series[0].Points.Select((DataPoint v) => v.XValue).Max().ToString("0.00");
			}
		}
		else if (plotType.Equals("TAscanSigma"))
		{
			RESchart.Titles[0].Text = "TDC TA scan Sigma";
			RESchart.Series.Clear();
			RESchart.ChartAreas[0].Name = "GenericXY";
			RESchart.ChartAreas[0].AxisX.Title = "Time Phase Expected (ps)";
			RESchart.ChartAreas[0].AxisX.Minimum = 0.0;
			RESchart.ChartAreas[0].AxisX.Maximum = 25000.0;
			RESchart.ChartAreas[0].AxisX.Interval = 1562.5;
			RESchart.ChartAreas[0].AxisY.Title = "TA sigma (ps)";
			RESchart.ChartAreas[0].AxisY.Minimum = double.NaN;
			RESchart.ChartAreas[0].AxisY.Maximum = double.NaN;
			RESchart.ChartAreas[0].AxisY.Interval = double.NaN;
			for (int num9 = num; num9 <= num2; num9++)
			{
				RESchart.Series.Add(num9.ToString());
				RESchart.Series[num9 - num].ChartType = SeriesChartType.Point;
				RESchart.Legends[0].Enabled = true;
				for (int num10 = 1; num10 <= 15; num10++)
				{
					StatFunction(num9, 0, num10, out var _, out var sigma4);
					RESchart.Series[num9 - num].Points.AddXY(25000.0 - (double)num10 * 1562.5, sigma4);
				}
			}
			if (num == num2)
			{
				Avg_txtBox.Text = RESchart.Series[0].Points.Select((DataPoint v) => v.YValues[0]).Average().ToString("0.00");
				Min_txtBox.Text = RESchart.Series[0].Points.Select((DataPoint v) => v.YValues[0]).Min().ToString("0.00");
				Max_txtBox.Text = RESchart.Series[0].Points.Select((DataPoint v) => v.YValues[0]).Max().ToString("0.00");
			}
		}
		else if (plotType.Equals("TAscanSigmaAvg"))
		{
			RESchart.Titles[0].Text = "TDC TA scan Sigma Avg";
			RESchart.Series.Clear();
			RESchart.Series.Add("Avg Sigma");
			RESchart.Series[0].ChartType = SeriesChartType.Point;
			RESchart.Legends[0].Enabled = true;
			RESchart.ChartAreas[0].Name = "GenericXY";
			RESchart.ChartAreas[0].AxisX.Title = "TDC number";
			RESchart.ChartAreas[0].AxisX.Minimum = 0.0;
			RESchart.ChartAreas[0].AxisX.Maximum = 15.0;
			RESchart.ChartAreas[0].AxisX.Interval = 1.0;
			RESchart.ChartAreas[0].AxisY.Title = "TA sigma (ps)";
			RESchart.ChartAreas[0].AxisY.Minimum = double.NaN;
			RESchart.ChartAreas[0].AxisY.Maximum = double.NaN;
			RESchart.ChartAreas[0].AxisY.Interval = double.NaN;
			double num11 = 0.0;
			for (int num12 = 0; num12 <= 15; num12++)
			{
				num11 = 0.0;
				for (int num13 = 1; num13 <= 15; num13++)
				{
					StatFunction(num12, 0, num13, out var _, out var sigma5);
					num11 += sigma5;
				}
				num11 /= 16.0;
				RESchart.Series[0].Points.AddXY(num12, num11);
			}
			Avg_txtBox.Text = RESchart.Series[0].Points.Select((DataPoint v) => v.YValues[0]).Average().ToString("0.00");
			Min_txtBox.Text = RESchart.Series[0].Points.Select((DataPoint v) => v.YValues[0]).Min().ToString("0.00");
			Max_txtBox.Text = RESchart.Series[0].Points.Select((DataPoint v) => v.YValues[0]).Max().ToString("0.00");
		}
		else if (plotType.Equals("DCOscan"))
		{
			RESchart.Titles[0].Text = "DCO scan";
			RESchart.Series.Clear();
			RESchart.ChartAreas[0].Name = "GenericXY";
			RESchart.ChartAreas[0].AxisX.Title = ((num == num2) ? "DCO code" : "DCO number");
			RESchart.ChartAreas[0].AxisX.Minimum = 0.0;
			RESchart.ChartAreas[0].AxisX.Maximum = ((num == num2) ? 64 : 16);
			RESchart.ChartAreas[0].AxisX.Interval = ((num != num2) ? 1 : 2);
			RESchart.ChartAreas[0].AxisY.Title = "DCO Period (ps)";
			RESchart.ChartAreas[0].AxisY.Minimum = 700.0;
			RESchart.ChartAreas[0].AxisY.Maximum = double.NaN;
			RESchart.ChartAreas[0].AxisY.Interval = 20.0;
			int num14 = 0;
			for (int num15 = num; num15 <= num2; num15++)
			{
				for (int num16 = num3; num16 <= num4; num16++)
				{
					RESchart.Series.Add("T" + num15 + "D" + num16).ToString();
					RESchart.Series[num14].ChartType = SeriesChartType.Point;
					RESchart.Legends[0].Enabled = true;
					for (int num17 = 0; num17 <= 63; num17++)
					{
						for (int num18 = 0; num18 < Convert.ToInt32(Cycle_txtBox.Text); num18++)
						{
							if (num == num2)
							{
								RESchart.Series[num14].Points.AddXY(num17, PerDco[num15, num16, num17, num18]);
							}
							else
							{
								RESchart.Series[num14].Points.AddXY((double)num15 + 0.2 * (double)num16, PerDco[num15, num16, num17, num18]);
							}
						}
					}
					num14++;
				}
			}
		}
		else if (plotType.Equals("DCOstep"))
		{
			RESchart.Titles[0].Text = "DCO Step";
			RESchart.Series.Clear();
			RESchart.ChartAreas[0].Name = "GenericXY";
			RESchart.ChartAreas[0].AxisX.Title = "DCO code";
			RESchart.ChartAreas[0].AxisX.Minimum = 0.0;
			RESchart.ChartAreas[0].AxisX.Maximum = 64.0;
			RESchart.ChartAreas[0].AxisX.Interval = 2.0;
			RESchart.ChartAreas[0].AxisY.Title = "DCO code step (ps)";
			RESchart.ChartAreas[0].AxisY.Minimum = double.NaN;
			RESchart.ChartAreas[0].AxisY.Maximum = double.NaN;
			RESchart.ChartAreas[0].AxisY.Interval = 4.0;
			int num19 = 0;
			for (int num20 = num; num20 <= num2; num20++)
			{
				for (int num21 = num3; num21 <= num4; num21++)
				{
					RESchart.Series.Add(("T" + num20 + "D" + num21).ToString());
					RESchart.Series[num19].ChartType = SeriesChartType.Point;
					RESchart.Legends[0].Enabled = true;
					for (int num22 = 1; num22 <= 63; num22++)
					{
						StatFunction(num20, num21, num22 - 1, out var avg4, out var sigma6);
						StatFunction(num20, num21, num22, out var avg5, out sigma6);
						RESchart.Series[num19].Points.AddXY(num22, avg5 - avg4);
					}
					num19++;
				}
			}
		}
		else if (plotType.Equals("DCOscanSigma"))
		{
			RESchart.Titles[0].Text = "DCO scan sigma";
			RESchart.Series.Clear();
			RESchart.ChartAreas[0].Name = "GenericXY";
			RESchart.ChartAreas[0].AxisX.Title = "DCO code";
			RESchart.ChartAreas[0].AxisX.Minimum = 0.0;
			RESchart.ChartAreas[0].AxisX.Maximum = 64.0;
			RESchart.ChartAreas[0].AxisX.Interval = 2.0;
			RESchart.ChartAreas[0].AxisY.Title = "DCO period Sigma (ps)";
			RESchart.ChartAreas[0].AxisY.Minimum = double.NaN;
			RESchart.ChartAreas[0].AxisY.Maximum = double.NaN;
			RESchart.ChartAreas[0].AxisY.Interval = double.NaN;
			int num23 = 0;
			for (int num24 = num; num24 <= num2; num24++)
			{
				for (int num25 = num3; num25 <= num4; num25++)
				{
					RESchart.Series.Add(("T" + num24 + "D" + num25).ToString());
					RESchart.Series[num23].ChartType = SeriesChartType.Point;
					RESchart.Legends[0].Enabled = true;
					for (int num26 = 1; num26 <= 63; num26++)
					{
						StatFunction(num24, num25, num26, out var _, out var sigma7);
						RESchart.Series[num23].Points.AddXY(num26, sigma7);
					}
					num23++;
				}
			}
		}
		else
		{
			if (!plotType.Equals("DCOPerVStime"))
			{
				return;
			}
			CodeSel_numUpDw.Visible = true;
			RESchart.Titles[0].Text = "DCO Period VS time";
			RESchart.Series.Clear();
			RESchart.ChartAreas[0].Name = "GenericXY";
			RESchart.ChartAreas[0].AxisX.Title = "Iteraction";
			RESchart.ChartAreas[0].AxisX.Minimum = 0.0;
			RESchart.ChartAreas[0].AxisX.Maximum = double.NaN;
			RESchart.ChartAreas[0].AxisX.Interval = double.NaN;
			RESchart.ChartAreas[0].AxisY.Title = "DCO period (ps)";
			RESchart.ChartAreas[0].AxisY.Minimum = 1000.0;
			RESchart.ChartAreas[0].AxisY.Maximum = 0.0;
			RESchart.ChartAreas[0].AxisY.Interval = double.NaN;
			RESchart.ChartAreas[0].AxisY.LabelStyle.Format = "0.000";
			int num27 = 0;
			double avg7 = 0.0;
			double sigma8 = 0.0;
			for (int num28 = num; num28 <= num2; num28++)
			{
				for (int num29 = num3; num29 <= num4; num29++)
				{
					RESchart.Series.Add(("T" + num28 + "D" + num29).ToString());
					RESchart.Series[num27].ChartType = SeriesChartType.Line;
					RESchart.Legends[0].Enabled = true;
					for (int num30 = (int)CodeSel_numUpDw.Value; num30 <= (int)CodeSel_numUpDw.Value; num30++)
					{
						StatFunction(num28, num29, num30, out avg7, out sigma8);
						for (int num31 = 0; num31 < Convert.ToInt32(Cycle_txtBox.Text); num31++)
						{
							RESchart.Series[num27].Points.AddXY(num31, PerDco[num28, num29, num30, num31]);
						}
					}
					double num32 = RESchart.Series[num27].Points.Select((DataPoint v) => v.YValues[0]).Min();
					double num33 = RESchart.Series[num27].Points.Select((DataPoint v) => v.YValues[0]).Max();
					RESchart.ChartAreas[0].AxisY.Minimum = ((num32 < RESchart.ChartAreas[0].AxisY.Minimum) ? num32 : RESchart.ChartAreas[0].AxisY.Minimum);
					RESchart.ChartAreas[0].AxisY.Maximum = ((num33 > RESchart.ChartAreas[0].AxisY.Maximum) ? num33 : RESchart.ChartAreas[0].AxisY.Maximum);
					TextBox min_txtBox = Min_txtBox;
					string obj;
					if (num != num2 || num3 != num4)
					{
						string text = (Min_txtBox.Text = "");
						obj = text;
					}
					else
					{
						obj = num32.ToString("0.000");
					}
					min_txtBox.Text = obj;
					TextBox max_txtBox = Max_txtBox;
					string obj2;
					if (num != num2 || num3 != num4)
					{
						string text = (Max_txtBox.Text = "");
						obj2 = text;
					}
					else
					{
						obj2 = num33.ToString("0.000");
					}
					max_txtBox.Text = obj2;
					TextBox avg_txtBox = Avg_txtBox;
					string obj3;
					if (num != num2 || num3 != num4)
					{
						string text = (Avg_txtBox.Text = "");
						obj3 = text;
					}
					else
					{
						obj3 = avg7.ToString("0.000");
					}
					avg_txtBox.Text = obj3;
					TextBox sigma_txtBox = Sigma_txtBox;
					string obj4;
					if (num != num2 || num3 != num4)
					{
						string text = (Sigma_txtBox.Text = "");
						obj4 = text;
					}
					else
					{
						obj4 = sigma8.ToString("0.000");
					}
					sigma_txtBox.Text = obj4;
					num27++;
				}
			}
		}
	}

	private void StatFunction(int TDC, int DCO, int Code, out double avg, out double sigma)
	{
		int i = 0;
		avg = 0.0;
		sigma = 0.0;
		if (TDCtest && !DCOtest && TDC < 16 && Code < 16)
		{
			for (; taTDC[TDC, Code, i] != -1.0; i++)
			{
				avg += taTDC[TDC, Code, i];
			}
			avg /= i;
			for (int j = 0; j < i; j++)
			{
				sigma += Math.Pow(taTDC[TDC, Code, j] - avg, 2.0);
			}
			sigma = Math.Sqrt(sigma / (double)(i - 1));
		}
		else if (DCOtest && !TDCtest && TDC < 16 && Code < 64)
		{
			for (; PerDco[TDC, DCO, Code, i] != -1.0; i++)
			{
				avg += PerDco[TDC, DCO, Code, i];
			}
			avg /= i;
			for (int k = 0; k < i; k++)
			{
				sigma += Math.Pow(PerDco[TDC, DCO, Code, k] - avg, 2.0);
			}
			sigma = Math.Sqrt(sigma / (double)(i - 1));
		}
	}

	private void Scanfitting(string nomeSeries, int TDCnum, out float mLine, out float qLine, out float sigma)
	{
		float[] array = new float[35001];
		float[] array2 = new float[35001];
		float num = 0f;
		float num2 = 0f;
		float num3 = 0f;
		float num4 = 0f;
		float num5 = 0f;
		int num6 = 0;
		if (nomeSeries == "TAscan")
		{
			num6 = RESchart.Series[0].Points.Count;
			for (int i = 0; i < Convert.ToInt32(Cycle_txtBox.Text); i++)
			{
				for (int j = 1; j <= 15; j++)
				{
					array[j - 1 + i * 15] = (float)(25000.0 - (double)j * 1562.5);
					array2[j - 1 + i * 15] = (float)taTDC[TDCnum, j, i];
				}
			}
		}
		else if (nomeSeries == "TAscanError")
		{
			num6 = RESchart.Series[0].Points.Count;
			for (int k = 0; k < Convert.ToInt32(Cycle_txtBox.Text); k++)
			{
				for (int l = 1; l <= 15; l++)
				{
					array[l - 1 + k * 15] = (float)(25000.0 - (double)l * 1562.5);
					array2[l - 1 + k * 15] = (float)(taTDC[TDCnum, l, k] - (25000.0 - (double)l * 1562.5));
				}
			}
		}
		for (int m = 0; m < num6; m++)
		{
			num += (float)Math.Pow(array[m], 2.0);
			num2 += array[m];
			num4 += array[m] * array2[m];
			num3 += array2[m];
		}
		qLine = (float)((double)(num * num3 - num2 * num4) / ((double)((float)num6 * num) - Math.Pow(num2, 2.0)));
		mLine = (float)((double)((float)num6 * num4 - num2 * num3) / ((double)((float)num6 * num) - Math.Pow(num2, 2.0)));
		for (int n = 0; n < num6; n++)
		{
			num5 += (float)Math.Pow(array2[n] - qLine - mLine * array[n], 2.0);
		}
		sigma = (float)Math.Sqrt(num5 / (float)(num6 - 2));
	}

	private void TAerr_HistFitting(int TDCnum, out float avg, out float sigma)
	{
		int[] array = new int[2000];
		int[] array2 = new int[2000];
		int num = 0;
		avg = 0f;
		sigma = 0f;
		for (int i = 1; i <= 15; i++)
		{
			for (int j = 0; j < Convert.ToInt32(Cycle_txtBox.Text); j++)
			{
				if ((int)(taTDC[TDCnum, i, j] - (25000.0 - (double)i * 1562.5)) >= 0)
				{
					array[(int)(taTDC[TDCnum, i, j] - (25000.0 - (double)i * 1562.5))]++;
				}
				else
				{
					array2[-(int)(taTDC[TDCnum, i, j] - (25000.0 - (double)i * 1562.5))]++;
				}
			}
		}
		for (int k = -1999; k <= 1999; k++)
		{
			if (k < 0)
			{
				num += array2[-k];
				avg += k * array2[-k];
			}
			else
			{
				num += array[k];
				avg += k * array[k];
			}
		}
		avg /= num;
		for (int l = -1999; l <= 1999; l++)
		{
			if (l < 0)
			{
				sigma += (float)(Math.Pow((float)l - avg, 2.0) * (double)array2[-l]);
			}
			else
			{
				sigma += (float)(Math.Pow((float)l - avg, 2.0) * (double)array[l]);
			}
		}
		sigma = (float)Math.Sqrt(sigma / (float)(num - 1));
	}

	private void ReadResFiles()
	{
		char[] separator = new char[2] { ' ', '\t' };
		int num = -1;
		if (TDCtest && !DCOtest)
		{
			initVectorData("TDC");
			initVectorData("TdcDco");
			PlotSel_comboBox.Items.Clear();
			PlotSel_comboBox.Items.Add("TDC scan TA");
			PlotSel_comboBox.Items.Add("TDC scan TA Errors");
			PlotSel_comboBox.Items.Add("TDC scan TA Errors Histo");
			PlotSel_comboBox.Items.Add("TDC scan TA sigma");
			PlotSel_comboBox.Items.Add("TDC scan TA sigma Avg");
		}
		else if (DCOtest && !TDCtest)
		{
			initVectorData("DCO");
			PlotSel_comboBox.Items.Clear();
			PlotSel_comboBox.Items.Add("DCO scan");
			PlotSel_comboBox.Items.Add("DCO scan Steps");
			PlotSel_comboBox.Items.Add("DCO scan sigma");
			PlotSel_comboBox.Items.Add("DCO Per vs T");
		}
		string[] array = files;
		foreach (string path in array)
		{
			int num2 = 0;
			num = -1;
			StreamReader streamReader = new StreamReader(path);
			while (!streamReader.EndOfStream)
			{
				string text = streamReader.ReadLine();
				if (text.Contains("TDC"))
				{
					continue;
				}
				text = text.Replace(',', '.');
				string[] array2 = text.Split(separator, StringSplitOptions.RemoveEmptyEntries);
				if (array2.Length == 19 && TDCtest && !DCOtest && array2[5].Equals("0"))
				{
					if (Convert.ToInt32(array2[8]) != num)
					{
						num2 = 0;
					}
					taTDC[Convert.ToInt32(array2[0]), Convert.ToInt32(array2[8]), num2] = Convert.ToDouble(array2[18]);
					TdcDcoPer[Convert.ToInt32(array2[0]), 0] = Convert.ToDouble(array2[16]);
					TdcDcoPer[Convert.ToInt32(array2[0]), 1] = Convert.ToDouble(array2[17]);
					num = Convert.ToInt32(array2[8]);
					num2++;
				}
				else if (array2.Length >= 17 && DCOtest && !TDCtest && array2[5].Equals("1") && array2[10].Equals("1"))
				{
					if ((array2[6].Equals("0") && Convert.ToInt32(array2[2]) != num) || (array2[6].Equals("1") && Convert.ToInt32(array2[4]) != num))
					{
						num2 = 0;
					}
					int num3 = 0;
					if (array2[6].Equals("0"))
					{
						num3 = 16 * Convert.ToInt32(array2[1]) + Convert.ToInt32(array2[2]);
						PerDco[Convert.ToInt32(array2[0]), Convert.ToInt32(array2[6]), num3, num2] = Convert.ToDouble(array2[16]);
					}
					else if (array2[6].Equals("1"))
					{
						num3 = 16 * Convert.ToInt32(array2[3]) + Convert.ToInt32(array2[4]);
						PerDco[Convert.ToInt32(array2[0]), Convert.ToInt32(array2[6]), num3, num2] = Convert.ToDouble(array2[17]);
					}
					num = (array2[6].Equals("0") ? Convert.ToInt32(array2[2]) : Convert.ToInt32(array2[4]));
					num2++;
				}
			}
			Cycle_txtBox.Text = num2.ToString();
		}
		if (TDCtest && !DCOtest)
		{
			PlotSel_comboBox.SelectedIndex = 0;
		}
		else if (DCOtest && !TDCtest)
		{
			PlotSel_comboBox.SelectedIndex = 0;
		}
	}

	private void openDirToolStripMenuItem_Click(object sender, EventArgs e)
	{
		dirResRun = "";
		openFileDialog1.Filter = "Text File|*.txt|All Files|*.*";
		openFileDialog1.Title = "Open test result file";
		if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
		{
			dirResRun = folderBrowserDialog1.SelectedPath;
			files = Directory.GetFiles(dirResRun, "IGNITE0*");
			Array.Sort(files);
			if (files[0].Contains("_DCO"))
			{
				DCOtest = true;
				TDCtest = false;
				DCOsel_comboBox.Enabled = true;
			}
			else
			{
				DCOtest = false;
				TDCtest = true;
				DCOsel_comboBox.Enabled = false;
			}
			ReadResFiles();
		}
	}

	private void PlotSel_comboBox_SelectedIndexChanged(object sender, EventArgs e)
	{
		PlotX_Min.Text = "";
		PlotX_Step.Text = "";
		PlotX_Max.Text = "";
		if (TDCtest && !DCOtest)
		{
			if (PlotSel_comboBox.SelectedIndex == 0)
			{
				buildPlot("TAscan");
			}
			else if (PlotSel_comboBox.SelectedIndex == 1)
			{
				buildPlot("TAscanError");
			}
			else if (PlotSel_comboBox.SelectedIndex == 2)
			{
				buildPlot("TAscanErrorHisto");
			}
			else if (PlotSel_comboBox.SelectedIndex == 3)
			{
				buildPlot("TAscanSigma");
			}
			else if (PlotSel_comboBox.SelectedIndex == 4)
			{
				buildPlot("TAscanSigmaAvg");
			}
		}
		else if (DCOtest && !TDCtest)
		{
			if (PlotSel_comboBox.SelectedIndex == 0)
			{
				buildPlot("DCOscan");
			}
			else if (PlotSel_comboBox.SelectedIndex == 1)
			{
				buildPlot("DCOstep");
			}
			else if (PlotSel_comboBox.SelectedIndex == 2)
			{
				buildPlot("DCOscanSigma");
			}
			else if (PlotSel_comboBox.SelectedIndex == 3)
			{
				buildPlot("DCOPerVStime");
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
		System.Windows.Forms.DataVisualization.Charting.ChartArea chartArea = new System.Windows.Forms.DataVisualization.Charting.ChartArea();
		System.Windows.Forms.DataVisualization.Charting.ChartArea chartArea2 = new System.Windows.Forms.DataVisualization.Charting.ChartArea();
		System.Windows.Forms.DataVisualization.Charting.Legend legend = new System.Windows.Forms.DataVisualization.Charting.Legend();
		System.Windows.Forms.DataVisualization.Charting.Title title = new System.Windows.Forms.DataVisualization.Charting.Title();
		System.Windows.Forms.DataVisualization.Charting.Title title2 = new System.Windows.Forms.DataVisualization.Charting.Title();
		this.RESchart = new System.Windows.Forms.DataVisualization.Charting.Chart();
		this.menuStrip1 = new System.Windows.Forms.MenuStrip();
		this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.loadRunToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
		this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.plotsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.dCOToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.tDCToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
		this.toolStripMenuItem2 = new System.Windows.Forms.ToolStripMenuItem();
		this.tDCScanToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
		this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
		this.PlotSel_comboBox = new System.Windows.Forms.ComboBox();
		this.label3 = new System.Windows.Forms.Label();
		this.Cycle_txtBox = new System.Windows.Forms.TextBox();
		this.label4 = new System.Windows.Forms.Label();
		this.label5 = new System.Windows.Forms.Label();
		this.TDCsel_comboBox = new System.Windows.Forms.ComboBox();
		this.DCOsel_comboBox = new System.Windows.Forms.ComboBox();
		this.label6 = new System.Windows.Forms.Label();
		this.CodeSel_numUpDw = new System.Windows.Forms.NumericUpDown();
		this.Sel_groupBox = new System.Windows.Forms.GroupBox();
		this.label1 = new System.Windows.Forms.Label();
		this.Info_groupBox = new System.Windows.Forms.GroupBox();
		this.DCOper_label = new System.Windows.Forms.Label();
		this.DcoRes_txtBox = new System.Windows.Forms.TextBox();
		this.label2 = new System.Windows.Forms.Label();
		this.DcoT1_txtBox = new System.Windows.Forms.TextBox();
		this.Date_txtBox = new System.Windows.Forms.TextBox();
		this.DcoT0_txtBox = new System.Windows.Forms.TextBox();
		this.groupBox1 = new System.Windows.Forms.GroupBox();
		this.label8 = new System.Windows.Forms.Label();
		this.LineSigma_txtBox = new System.Windows.Forms.TextBox();
		this.LineInterc_txtBox = new System.Windows.Forms.TextBox();
		this.LineAngCoef_txtBox = new System.Windows.Forms.TextBox();
		this.label7 = new System.Windows.Forms.Label();
		this.Sigma_txtBox = new System.Windows.Forms.TextBox();
		this.Max_txtBox = new System.Windows.Forms.TextBox();
		this.Min_txtBox = new System.Windows.Forms.TextBox();
		this.Avg_txtBox = new System.Windows.Forms.TextBox();
		this.groupBox2 = new System.Windows.Forms.GroupBox();
		this.PlotX_Step = new System.Windows.Forms.TextBox();
		this.PlotX_Max = new System.Windows.Forms.TextBox();
		this.PlotX_Min = new System.Windows.Forms.TextBox();
		((System.ComponentModel.ISupportInitialize)this.RESchart).BeginInit();
		this.menuStrip1.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.CodeSel_numUpDw).BeginInit();
		this.Sel_groupBox.SuspendLayout();
		this.Info_groupBox.SuspendLayout();
		this.groupBox1.SuspendLayout();
		this.groupBox2.SuspendLayout();
		base.SuspendLayout();
		chartArea.AxisX.Interval = 2.0;
		chartArea.AxisX.MajorGrid.LineDashStyle = System.Windows.Forms.DataVisualization.Charting.ChartDashStyle.Dot;
		chartArea.AxisX.Maximum = 48.0;
		chartArea.AxisX.Minimum = 0.0;
		chartArea.AxisX.Title = "DCO settings";
		chartArea.AxisY.Interval = 100.0;
		chartArea.AxisY.LineDashStyle = System.Windows.Forms.DataVisualization.Charting.ChartDashStyle.Dot;
		chartArea.AxisY.Maximum = 2000.0;
		chartArea.AxisY.Minimum = 0.0;
		chartArea.AxisY.Title = "DCO period (ps)";
		chartArea.CursorX.Interval = 0.0;
		chartArea.CursorX.IntervalOffset = 1.0;
		chartArea.CursorX.IsUserSelectionEnabled = true;
		chartArea.CursorY.IsUserSelectionEnabled = true;
		chartArea.Name = "ChartArea1";
		chartArea2.Name = "ChartArea2";
		chartArea2.Visible = false;
		this.RESchart.ChartAreas.Add(chartArea);
		this.RESchart.ChartAreas.Add(chartArea2);
		legend.Alignment = System.Drawing.StringAlignment.Far;
		legend.Enabled = false;
		legend.IsDockedInsideChartArea = false;
		legend.Name = "Legend1";
		this.RESchart.Legends.Add(legend);
		this.RESchart.Location = new System.Drawing.Point(3, 37);
		this.RESchart.Name = "RESchart";
		this.RESchart.Size = new System.Drawing.Size(758, 540);
		this.RESchart.TabIndex = 1;
		this.RESchart.Text = "chart1";
		title.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		title.Name = "Title1";
		title.Text = "DCO test scan";
		title2.Name = "Title2";
		this.RESchart.Titles.Add(title);
		this.RESchart.Titles.Add(title2);
		this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[2] { this.fileToolStripMenuItem, this.plotsToolStripMenuItem });
		this.menuStrip1.Location = new System.Drawing.Point(0, 0);
		this.menuStrip1.Name = "menuStrip1";
		this.menuStrip1.Size = new System.Drawing.Size(962, 24);
		this.menuStrip1.TabIndex = 2;
		this.menuStrip1.Text = "menuStrip1";
		this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[3] { this.loadRunToolStripMenuItem, this.toolStripSeparator2, this.exitToolStripMenuItem });
		this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
		this.fileToolStripMenuItem.Size = new System.Drawing.Size(37, 20);
		this.fileToolStripMenuItem.Text = "File";
		this.loadRunToolStripMenuItem.Name = "loadRunToolStripMenuItem";
		this.loadRunToolStripMenuItem.Size = new System.Drawing.Size(124, 22);
		this.loadRunToolStripMenuItem.Text = "Load Run";
		this.loadRunToolStripMenuItem.Click += new System.EventHandler(openDirToolStripMenuItem_Click);
		this.toolStripSeparator2.Name = "toolStripSeparator2";
		this.toolStripSeparator2.Size = new System.Drawing.Size(121, 6);
		this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
		this.exitToolStripMenuItem.Size = new System.Drawing.Size(124, 22);
		this.exitToolStripMenuItem.Text = "Exit";
		this.plotsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[5] { this.dCOToolStripMenuItem, this.tDCToolStripMenuItem, this.toolStripSeparator1, this.toolStripMenuItem2, this.tDCScanToolStripMenuItem });
		this.plotsToolStripMenuItem.Name = "plotsToolStripMenuItem";
		this.plotsToolStripMenuItem.Size = new System.Drawing.Size(45, 20);
		this.plotsToolStripMenuItem.Text = "Plots";
		this.dCOToolStripMenuItem.Name = "dCOToolStripMenuItem";
		this.dCOToolStripMenuItem.Size = new System.Drawing.Size(130, 22);
		this.dCOToolStripMenuItem.Text = "DCO Point";
		this.tDCToolStripMenuItem.Name = "tDCToolStripMenuItem";
		this.tDCToolStripMenuItem.Size = new System.Drawing.Size(130, 22);
		this.tDCToolStripMenuItem.Text = "DCO Scan";
		this.toolStripSeparator1.Name = "toolStripSeparator1";
		this.toolStripSeparator1.Size = new System.Drawing.Size(127, 6);
		this.toolStripMenuItem2.Name = "toolStripMenuItem2";
		this.toolStripMenuItem2.Size = new System.Drawing.Size(130, 22);
		this.toolStripMenuItem2.Text = "TDC Point";
		this.tDCScanToolStripMenuItem.Name = "tDCScanToolStripMenuItem";
		this.tDCScanToolStripMenuItem.Size = new System.Drawing.Size(130, 22);
		this.tDCScanToolStripMenuItem.Text = "TDC scan";
		this.openFileDialog1.FileName = "openFileDialog1";
		this.PlotSel_comboBox.FormattingEnabled = true;
		this.PlotSel_comboBox.Location = new System.Drawing.Point(6, 67);
		this.PlotSel_comboBox.Name = "PlotSel_comboBox";
		this.PlotSel_comboBox.Size = new System.Drawing.Size(171, 21);
		this.PlotSel_comboBox.TabIndex = 7;
		this.PlotSel_comboBox.SelectedIndexChanged += new System.EventHandler(PlotSel_comboBox_SelectedIndexChanged);
		this.label3.AutoSize = true;
		this.label3.Location = new System.Drawing.Point(3, 53);
		this.label3.Name = "label3";
		this.label3.Size = new System.Drawing.Size(43, 13);
		this.label3.TabIndex = 8;
		this.label3.Text = "Plot Sel";
		this.Cycle_txtBox.Location = new System.Drawing.Point(107, 13);
		this.Cycle_txtBox.Name = "Cycle_txtBox";
		this.Cycle_txtBox.ReadOnly = true;
		this.Cycle_txtBox.Size = new System.Drawing.Size(87, 20);
		this.Cycle_txtBox.TabIndex = 9;
		this.label4.AutoSize = true;
		this.label4.Location = new System.Drawing.Point(8, 16);
		this.label4.Name = "label4";
		this.label4.Size = new System.Drawing.Size(93, 13);
		this.label4.TabIndex = 10;
		this.label4.Text = "Num of Iteractions";
		this.label5.AutoSize = true;
		this.label5.Location = new System.Drawing.Point(0, 15);
		this.label5.Name = "label5";
		this.label5.Size = new System.Drawing.Size(74, 13);
		this.label5.TabIndex = 12;
		this.label5.Text = "TDC selection";
		this.TDCsel_comboBox.FormattingEnabled = true;
		this.TDCsel_comboBox.Items.AddRange(new object[17]
		{
			"TDC 0", "TDC 1", "TDC 2", "TDC 3", "TDC 4", "TDC 5", "TDC 6", "TDC 7", "TDC 8", "TDC 9",
			"TDC 10", "TDC 11", "TDC 12", "TDC 13", "TDC 14", "TDC 15", "TDC ALL"
		});
		this.TDCsel_comboBox.Location = new System.Drawing.Point(3, 31);
		this.TDCsel_comboBox.Name = "TDCsel_comboBox";
		this.TDCsel_comboBox.Size = new System.Drawing.Size(70, 21);
		this.TDCsel_comboBox.TabIndex = 11;
		this.TDCsel_comboBox.SelectedIndexChanged += new System.EventHandler(PlotSel_comboBox_SelectedIndexChanged);
		this.DCOsel_comboBox.FormattingEnabled = true;
		this.DCOsel_comboBox.Items.AddRange(new object[3] { "DCO 0", "DCO 1", "DCO ALL" });
		this.DCOsel_comboBox.Location = new System.Drawing.Point(79, 31);
		this.DCOsel_comboBox.Name = "DCOsel_comboBox";
		this.DCOsel_comboBox.Size = new System.Drawing.Size(70, 21);
		this.DCOsel_comboBox.TabIndex = 13;
		this.DCOsel_comboBox.SelectedIndexChanged += new System.EventHandler(PlotSel_comboBox_SelectedIndexChanged);
		this.label6.AutoSize = true;
		this.label6.Location = new System.Drawing.Point(79, 15);
		this.label6.Name = "label6";
		this.label6.Size = new System.Drawing.Size(75, 13);
		this.label6.TabIndex = 14;
		this.label6.Text = "DCO selection";
		this.CodeSel_numUpDw.Location = new System.Drawing.Point(155, 31);
		this.CodeSel_numUpDw.Maximum = new decimal(new int[4] { 63, 0, 0, 0 });
		this.CodeSel_numUpDw.Name = "CodeSel_numUpDw";
		this.CodeSel_numUpDw.Size = new System.Drawing.Size(38, 20);
		this.CodeSel_numUpDw.TabIndex = 15;
		this.CodeSel_numUpDw.Visible = false;
		this.CodeSel_numUpDw.ValueChanged += new System.EventHandler(PlotSel_comboBox_SelectedIndexChanged);
		this.Sel_groupBox.BackColor = System.Drawing.Color.LightGreen;
		this.Sel_groupBox.Controls.Add(this.label1);
		this.Sel_groupBox.Controls.Add(this.PlotSel_comboBox);
		this.Sel_groupBox.Controls.Add(this.label3);
		this.Sel_groupBox.Controls.Add(this.CodeSel_numUpDw);
		this.Sel_groupBox.Controls.Add(this.TDCsel_comboBox);
		this.Sel_groupBox.Controls.Add(this.label6);
		this.Sel_groupBox.Controls.Add(this.label5);
		this.Sel_groupBox.Controls.Add(this.DCOsel_comboBox);
		this.Sel_groupBox.Location = new System.Drawing.Point(767, 37);
		this.Sel_groupBox.Name = "Sel_groupBox";
		this.Sel_groupBox.Size = new System.Drawing.Size(195, 96);
		this.Sel_groupBox.TabIndex = 16;
		this.Sel_groupBox.TabStop = false;
		this.Sel_groupBox.Text = "Selections";
		this.label1.AutoSize = true;
		this.label1.Location = new System.Drawing.Point(152, 15);
		this.label1.Name = "label1";
		this.label1.Size = new System.Drawing.Size(32, 13);
		this.label1.TabIndex = 17;
		this.label1.Text = "Code";
		this.Info_groupBox.BackColor = System.Drawing.Color.LightYellow;
		this.Info_groupBox.Controls.Add(this.DCOper_label);
		this.Info_groupBox.Controls.Add(this.DcoRes_txtBox);
		this.Info_groupBox.Controls.Add(this.label2);
		this.Info_groupBox.Controls.Add(this.DcoT1_txtBox);
		this.Info_groupBox.Controls.Add(this.Date_txtBox);
		this.Info_groupBox.Controls.Add(this.label4);
		this.Info_groupBox.Controls.Add(this.DcoT0_txtBox);
		this.Info_groupBox.Controls.Add(this.Cycle_txtBox);
		this.Info_groupBox.Location = new System.Drawing.Point(767, 139);
		this.Info_groupBox.Name = "Info_groupBox";
		this.Info_groupBox.Size = new System.Drawing.Size(195, 109);
		this.Info_groupBox.TabIndex = 17;
		this.Info_groupBox.TabStop = false;
		this.Info_groupBox.Text = "RUN info";
		this.DCOper_label.AutoSize = true;
		this.DCOper_label.Location = new System.Drawing.Point(6, 62);
		this.DCOper_label.Name = "DCOper_label";
		this.DCOper_label.Size = new System.Drawing.Size(173, 13);
		this.DCOper_label.TabIndex = 14;
		this.DCOper_label.Text = "DCOs        T0           T1            Res";
		this.DcoRes_txtBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 7f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.DcoRes_txtBox.Location = new System.Drawing.Point(145, 78);
		this.DcoRes_txtBox.Name = "DcoRes_txtBox";
		this.DcoRes_txtBox.ReadOnly = true;
		this.DcoRes_txtBox.Size = new System.Drawing.Size(46, 18);
		this.DcoRes_txtBox.TabIndex = 13;
		this.label2.AutoSize = true;
		this.label2.Location = new System.Drawing.Point(8, 42);
		this.label2.Name = "label2";
		this.label2.Size = new System.Drawing.Size(53, 13);
		this.label2.TabIndex = 12;
		this.label2.Text = "Run Date";
		this.DcoT1_txtBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 7f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.DcoT1_txtBox.Location = new System.Drawing.Point(97, 78);
		this.DcoT1_txtBox.Name = "DcoT1_txtBox";
		this.DcoT1_txtBox.ReadOnly = true;
		this.DcoT1_txtBox.Size = new System.Drawing.Size(46, 18);
		this.DcoT1_txtBox.TabIndex = 12;
		this.Date_txtBox.Location = new System.Drawing.Point(84, 39);
		this.Date_txtBox.Name = "Date_txtBox";
		this.Date_txtBox.ReadOnly = true;
		this.Date_txtBox.Size = new System.Drawing.Size(110, 20);
		this.Date_txtBox.TabIndex = 11;
		this.DcoT0_txtBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 7f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.DcoT0_txtBox.Location = new System.Drawing.Point(48, 78);
		this.DcoT0_txtBox.Name = "DcoT0_txtBox";
		this.DcoT0_txtBox.ReadOnly = true;
		this.DcoT0_txtBox.Size = new System.Drawing.Size(46, 18);
		this.DcoT0_txtBox.TabIndex = 10;
		this.groupBox1.BackColor = System.Drawing.Color.LightYellow;
		this.groupBox1.Controls.Add(this.label8);
		this.groupBox1.Controls.Add(this.LineSigma_txtBox);
		this.groupBox1.Controls.Add(this.LineInterc_txtBox);
		this.groupBox1.Controls.Add(this.LineAngCoef_txtBox);
		this.groupBox1.Controls.Add(this.label7);
		this.groupBox1.Controls.Add(this.Sigma_txtBox);
		this.groupBox1.Controls.Add(this.Max_txtBox);
		this.groupBox1.Controls.Add(this.Min_txtBox);
		this.groupBox1.Controls.Add(this.Avg_txtBox);
		this.groupBox1.Location = new System.Drawing.Point(767, 254);
		this.groupBox1.Name = "groupBox1";
		this.groupBox1.Size = new System.Drawing.Size(195, 239);
		this.groupBox1.TabIndex = 18;
		this.groupBox1.TabStop = false;
		this.groupBox1.Text = "Statistic";
		this.label8.AutoSize = true;
		this.label8.Location = new System.Drawing.Point(58, 60);
		this.label8.Name = "label8";
		this.label8.Size = new System.Drawing.Size(128, 13);
		this.label8.TabIndex = 9;
		this.label8.Text = "M             Q            Sigma";
		this.LineSigma_txtBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 7f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.LineSigma_txtBox.Location = new System.Drawing.Point(147, 76);
		this.LineSigma_txtBox.Name = "LineSigma_txtBox";
		this.LineSigma_txtBox.ReadOnly = true;
		this.LineSigma_txtBox.Size = new System.Drawing.Size(46, 18);
		this.LineSigma_txtBox.TabIndex = 8;
		this.LineInterc_txtBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 7f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.LineInterc_txtBox.Location = new System.Drawing.Point(99, 76);
		this.LineInterc_txtBox.Name = "LineInterc_txtBox";
		this.LineInterc_txtBox.ReadOnly = true;
		this.LineInterc_txtBox.Size = new System.Drawing.Size(46, 18);
		this.LineInterc_txtBox.TabIndex = 7;
		this.LineAngCoef_txtBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 7f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.LineAngCoef_txtBox.Location = new System.Drawing.Point(50, 76);
		this.LineAngCoef_txtBox.Name = "LineAngCoef_txtBox";
		this.LineAngCoef_txtBox.ReadOnly = true;
		this.LineAngCoef_txtBox.Size = new System.Drawing.Size(46, 18);
		this.LineAngCoef_txtBox.TabIndex = 6;
		this.label7.AutoSize = true;
		this.label7.Location = new System.Drawing.Point(8, 20);
		this.label7.Name = "label7";
		this.label7.Size = new System.Drawing.Size(179, 13);
		this.label7.TabIndex = 5;
		this.label7.Text = "Min           Avg          Max        Sigma";
		this.Sigma_txtBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 7f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.Sigma_txtBox.Location = new System.Drawing.Point(147, 36);
		this.Sigma_txtBox.Name = "Sigma_txtBox";
		this.Sigma_txtBox.ReadOnly = true;
		this.Sigma_txtBox.Size = new System.Drawing.Size(46, 18);
		this.Sigma_txtBox.TabIndex = 4;
		this.Max_txtBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 7f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.Max_txtBox.Location = new System.Drawing.Point(99, 36);
		this.Max_txtBox.Name = "Max_txtBox";
		this.Max_txtBox.ReadOnly = true;
		this.Max_txtBox.Size = new System.Drawing.Size(46, 18);
		this.Max_txtBox.TabIndex = 3;
		this.Min_txtBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 7f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.Min_txtBox.Location = new System.Drawing.Point(2, 36);
		this.Min_txtBox.Name = "Min_txtBox";
		this.Min_txtBox.ReadOnly = true;
		this.Min_txtBox.Size = new System.Drawing.Size(46, 18);
		this.Min_txtBox.TabIndex = 2;
		this.Avg_txtBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 7f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.Avg_txtBox.Location = new System.Drawing.Point(50, 36);
		this.Avg_txtBox.Name = "Avg_txtBox";
		this.Avg_txtBox.ReadOnly = true;
		this.Avg_txtBox.Size = new System.Drawing.Size(46, 18);
		this.Avg_txtBox.TabIndex = 1;
		this.groupBox2.Controls.Add(this.PlotX_Min);
		this.groupBox2.Controls.Add(this.PlotX_Max);
		this.groupBox2.Controls.Add(this.PlotX_Step);
		this.groupBox2.Location = new System.Drawing.Point(766, 499);
		this.groupBox2.Name = "groupBox2";
		this.groupBox2.Size = new System.Drawing.Size(195, 77);
		this.groupBox2.TabIndex = 19;
		this.groupBox2.TabStop = false;
		this.groupBox2.Text = "Plot Axis Ctrls";
		this.PlotX_Step.Location = new System.Drawing.Point(87, 51);
		this.PlotX_Step.Name = "PlotX_Step";
		this.PlotX_Step.Size = new System.Drawing.Size(51, 20);
		this.PlotX_Step.TabIndex = 0;
		this.PlotX_Max.Location = new System.Drawing.Point(141, 51);
		this.PlotX_Max.Name = "PlotX_Max";
		this.PlotX_Max.Size = new System.Drawing.Size(51, 20);
		this.PlotX_Max.TabIndex = 1;
		this.PlotX_Min.Location = new System.Drawing.Point(31, 51);
		this.PlotX_Min.Name = "PlotX_Min";
		this.PlotX_Min.Size = new System.Drawing.Size(51, 20);
		this.PlotX_Min.TabIndex = 2;
		base.AutoScaleDimensions = new System.Drawing.SizeF(6f, 13f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		base.ClientSize = new System.Drawing.Size(962, 588);
		base.Controls.Add(this.groupBox2);
		base.Controls.Add(this.groupBox1);
		base.Controls.Add(this.Info_groupBox);
		base.Controls.Add(this.Sel_groupBox);
		base.Controls.Add(this.RESchart);
		base.Controls.Add(this.menuStrip1);
		base.MainMenuStrip = this.menuStrip1;
		base.Name = "PlotResult";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "Plot results";
		((System.ComponentModel.ISupportInitialize)this.RESchart).EndInit();
		this.menuStrip1.ResumeLayout(false);
		this.menuStrip1.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.CodeSel_numUpDw).EndInit();
		this.Sel_groupBox.ResumeLayout(false);
		this.Sel_groupBox.PerformLayout();
		this.Info_groupBox.ResumeLayout(false);
		this.Info_groupBox.PerformLayout();
		this.groupBox1.ResumeLayout(false);
		this.groupBox1.PerformLayout();
		this.groupBox2.ResumeLayout(false);
		this.groupBox2.PerformLayout();
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
