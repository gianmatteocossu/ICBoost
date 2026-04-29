using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MathNet.Numerics.Statistics;
using ScottPlot;
using ScottPlot.Grids;
using ScottPlot.Hatches;
using ScottPlot.Palettes;
using ScottPlot.Plottables;
using ScottPlot.Statistics;
using ScottPlot.WinForms;

namespace tb_Ignite64;

public class DaqFifoForm : Form
{
	private readonly FormsPlot Variable_Plot;

	private bool PLOT_ToggleStateIsCAL;

	private int plot_counter;

	private bool reduced_sigma;

	private double z_tolerance;

	private MainForm MyMain;

	public CancellationTokenSource STOP_Token_Source;

	private IContainer components;

	private MenuStrip DaqFifoForm_menuStrip;

	private ToolStripMenuItem fileToolStripMenuItem;

	private ToolStripMenuItem saveToolStripMenuItem;

	private ToolStripSeparator toolStripMenuItem1;

	private ToolStripMenuItem exitToolStripMenuItem;

	private Button ReadSingleFifo_but;

	private SaveFileDialog FIFO_saveFileDialog;

	private Button FifoReadRange_but;

	private NumericUpDown FifoNWord_UpDown;

	private ToolTip toolTip1;

	private Button ReadDataToGRID_but;

	private Button ReadingSTOP_but;

	private Button ReadDataToFILE_but;

	private Button LoadDataToGRID_but;

	private Button LoadDataToMEM_but;

	private Panel Plot_Panel;

	private Button PLOT_PlotGenerate_but;

	private Button PLOT_ToggleDataType_but;

	private Label PLOT_ToggleCAL_label;

	private Label PLOT_ToggleMES_label;

	private ComboBox PLOT_PlotType_comboBox;

	private Label PLOT_PlotType_label;

	private ComboBox PLOT_XAxis_comboBox;

	private Label PLOT_XAxis_label;

	private Label PLOT_YAxis_label;

	private ComboBox PLOT_YAxis_comboBox;

	private CheckBox PLOT_MatSelectALL_chkBox;

	private NumericUpDown PLOT_MatSelect_UpDown;

	private Label PLOT_MatSelect_label;

	private CheckBox PLOT_PixSelectALL_chkBox;

	private NumericUpDown PLOT_PixSelect_UpDown;

	private Label PLOT_PixSelect_label;

	private Button PLOT_PlotClear_but;

	private NumericUpDown PLOT_BinCount_UpDown;

	private Label PLOT_BinCount_label;

	private CheckBox PLOT_HistNormalize_chkBox;

	private Button SaveGRIDtoFILE_but;

	private Button button1;

	private DataGridViewTextBoxColumn RAW;

	private DataGridViewTextBoxColumn SerFifoStatus;

	private DataGridViewTextBoxColumn QuadFIFO;

	private DataGridViewTextBoxColumn MAT_ADDRESS;

	private DataGridViewTextBoxColumn PIX_ADDRESS;

	private DataGridViewTextBoxColumn TPM;

	private DataGridViewTextBoxColumn CALIB;

	private DataGridViewTextBoxColumn Timestamp;

	private DataGridViewTextBoxColumn CAL_DCO;

	private DataGridViewTextBoxColumn CAL_DE;

	private DataGridViewTextBoxColumn CAL_TIME;

	private DataGridViewTextBoxColumn COUNT_1;

	private DataGridViewTextBoxColumn COUNT_0;

	private DataGridViewTextBoxColumn COUNT_TOT;

	private DataGridViewTextBoxColumn DCO0_CODE;

	private DataGridViewTextBoxColumn DCO1_CODE;

	private Button ClearGRIDonly_but;

	public CheckBox FIFO_DropFakeData_chkBox;

	private Button FifoReadEmpty_but;

	private TextBox PlotAvg1_textBox;

	private ContextMenuStrip contextMenuStrip1;

	private TextBox PlotSigma1_textBox;

	private Label PlotAvg_label;

	private Label PlotSigma_label;

	private DataGridView DaqFifo_dGrid;

	private NumericUpDown PLOT_PixSelect2_UpDown;

	private Label PLOT_PixSelect2_label;

	private NumericUpDown PLOT_TAcode_UpDown;

	private Label PLOT_TAcode_label;

	private Button SaveALLtoFILE_but;

	private CheckBox PLOT_RemoveOutliers_chkBox;

	private NumericUpDown PLOT_RemoveOutliers_numUpDown;

	private Label PLOT_RemoveOutliers_label;

	public DaqFifoForm(MainForm TopForm)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Expected O, but got Unknown
		//IL_0012: Expected O, but got Unknown
		FormsPlot val = new FormsPlot();
		((Control)val).Dock = DockStyle.Fill;
		Variable_Plot = val;
		PLOT_ToggleStateIsCAL = true;
		z_tolerance = 3.0;
		base._002Ector();
		InitializeComponent();
		MyMain = TopForm;
		STOP_Token_Source = new CancellationTokenSource();
		Initialize_UI_Components();
	}

	public void CancelStopToken()
	{
		CancellationTokenSource sTOP_Token_Source = STOP_Token_Source;
		if (sTOP_Token_Source != null && !sTOP_Token_Source.IsCancellationRequested)
		{
			try
			{
				sTOP_Token_Source.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
		}
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		try
		{
			STOP_Token_Source?.Cancel();
			STOP_Token_Source?.Dispose();
		}
		catch
		{
		}
		STOP_Token_Source = null;
		base.OnFormClosed(e);
	}

	public async Task FifoReadNum(int NWords)
	{
		NWords--;
		if (NWords < 0 || NWords > 23)
		{
			MessageBox.Show("ERROR: NWords outside bounds");
			return;
		}
		ulong[] array = await MyMain.FifoReadNumWords(NWords);
		if (array.Length == 0 || array[0] == 0L)
		{
			return;
		}
		ulong[] array2 = array;
		foreach (ulong num in array2)
		{
			if (!FIFO_DropFakeData_chkBox.Checked || ((num >> 48) & 0xFF) >= 1)
			{
				DaqFifoGridFill(num);
				MainForm.LoadedData.AddDataFromRaw(num, MyMain);
			}
		}
	}

	private async Task ReadContinuouslyAsync(CancellationToken STOP_Token)
	{
		int N_read = 0;
		while (true)
		{
			STOP_Token.ThrowIfCancellationRequested();
			N_read += await ReadUntilEmpty();
			FillGrid_fromMemory(N_read);
			N_read = 0;
			ShowLastVisibleRows();
			await Task.Delay(1);
		}
	}

	private async Task ReadContinuouslyToFileAsync(StreamWriter writer, CancellationToken STOP_Token)
	{
		int[] columnWidths = new int[DaqFifo_dGrid.Columns.Count];
		for (int i = 0; i < DaqFifo_dGrid.Columns.Count; i++)
		{
			columnWidths[i] = DaqFifo_dGrid.Columns[i].HeaderText.Length;
			if (i == 0)
			{
				columnWidths[0] += 8;
			}
		}
		for (int j = 0; j < DaqFifo_dGrid.Columns.Count; j++)
		{
			await writer.WriteAsync(DaqFifo_dGrid.Columns[j].HeaderText.PadRight(columnWidths[j] + 2));
		}
		await writer.WriteLineAsync();
		while (true)
		{
			STOP_Token.ThrowIfCancellationRequested();
			ulong DataFifoRaw = await Task.Run(() => MyMain.FifoReadSingle());
			if (DataFifoRaw != 0L && (!FIFO_DropFakeData_chkBox.Checked || ((DataFifoRaw >> 48) & 0xFF) >= 1))
			{
				object[] DataTranslated = await Task.Run(() => MyMain.RawDataToObjArray(DataFifoRaw));
				int j = DataTranslated.Length;
				int pix = Convert.ToInt32(DataTranslated[4]);
				for (int i2 = 0; i2 < j; i2++)
				{
					await writer.WriteAsync(DataTranslated[i2].ToString().PadRight(columnWidths[i2] + 2));
				}
				await writer.WriteAsync((((Convert.ToInt32(MyMain.Mat_dGridView.Rows[4].Cells[0].Value.ToString(), 16) >> 4) & 3) * 16 + (Convert.ToInt32(MyMain.Mat_dGridView.Rows[4].Cells[0].Value.ToString(), 16) & 0xF)).ToString().PadRight(11));
				await writer.WriteAsync((((Convert.ToInt32(MyMain.Mat_dGridView.Rows[Convert.ToInt32(pix) / 16].Cells[Convert.ToInt32(pix) % 16].Value.ToString(), 16) >> 4) & 3) * 16 + (Convert.ToInt32(MyMain.Mat_dGridView.Rows[Convert.ToInt32(pix) / 16].Cells[Convert.ToInt32(pix) % 16].Value.ToString(), 16) & 0xF)).ToString().PadRight(11));
				await writer.WriteLineAsync();
				await Task.Delay(10);
			}
		}
	}

	private async Task<int> ReadUntilEmpty(bool gridlimit = false, int delay = 0)
	{
		int n_read = 0;
		bool keep_going = true;
		while (keep_going)
		{
			ulong FifoRaw = await Task.Run(() => MyMain.FifoReadSingle());
			int num = Convert.ToInt32((FifoRaw >> 48) & 0xFF);
			if (num < 1)
			{
				keep_going = false;
				continue;
			}
			await Task.Run(delegate
			{
				MainForm.LoadedData.AddDataFromRaw(FifoRaw, MyMain);
			});
			n_read++;
			await Task.Delay(delay);
			if (gridlimit && n_read > 1023)
			{
				keep_going = false;
			}
			if (n_read > 1024)
			{
				delay += 2;
			}
		}
		return n_read;
	}

	private void DaqFifoGridFill(ulong DataFifoRaw)
	{
		if (DaqFifo_dGrid.InvokeRequired)
		{
			DaqFifo_dGrid.BeginInvoke(new Action<ulong>(DaqFifoGridFill), DataFifoRaw);
		}
		else if (!FIFO_DropFakeData_chkBox.Checked || ((DataFifoRaw >> 48) & 0xFF) >= 1)
		{
			DaqFifo_dGrid.Rows.Add();
			DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Height = 19;
			DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].HeaderCell.Value = (DaqFifo_dGrid.RowCount - 1).ToString();
			DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
			DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[0].Value = DataFifoRaw.ToString("X16");
			DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[1].Value = (DataFifoRaw >> 47) & 1;
			DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[2].Value = (DataFifoRaw >> 48) & 0xFF;
			DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[3].Value = (DataFifoRaw >> 43) & 0xF;
			int value = Convert.ToInt32(((DataFifoRaw >> 40) & 7) * 8 + ((DataFifoRaw >> 37) & 7));
			DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[4].Value = ((DataFifoRaw >> 40) & 7) * 8 + ((DataFifoRaw >> 37) & 7);
			DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[5].Value = (DataFifoRaw >> 36) & 1;
			DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[6].Value = (DataFifoRaw >> 35) & 1;
			DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[7].Value = (DataFifoRaw >> 26) & 0x1FF;
			if (Convert.ToBoolean((DataFifoRaw >> 35) & 1))
			{
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[8].Value = (DataFifoRaw >> 16) & 1;
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[9].Value = (DataFifoRaw >> 15) & 1;
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[10].Value = (DataFifoRaw >> 13) & 3;
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[11].Value = "-";
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[12].Value = "-";
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[13].Value = DataFifoRaw & 0x1FFF;
			}
			else
			{
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[8].Value = "-";
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[9].Value = "-";
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[10].Value = "-";
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[11].Value = (DataFifoRaw >> 17) & 0x1FF;
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[12].Value = (DataFifoRaw >> 8) & 0x1FF;
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[13].Value = DataFifoRaw & 0xFF;
			}
			DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[14].Value = ((Convert.ToInt32(MyMain.Mat_dGridView.Rows[4].Cells[0].Value.ToString(), 16) >> 4) & 3) * 16 + (Convert.ToInt32(MyMain.Mat_dGridView.Rows[4].Cells[0].Value.ToString(), 16) & 0xF);
			DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[15].Value = ((Convert.ToInt32(MyMain.Mat_dGridView.Rows[Convert.ToInt32(value) / 16].Cells[Convert.ToInt32(value) % 16].Value.ToString(), 16) >> 4) & 3) * 16 + (Convert.ToInt32(MyMain.Mat_dGridView.Rows[Convert.ToInt32(value) / 16].Cells[Convert.ToInt32(value) % 16].Value.ToString(), 16) & 0xF);
		}
	}

	private void FillGrid_fromMemory(int N = int.MaxValue)
	{
		if (DaqFifo_dGrid.InvokeRequired)
		{
			DaqFifo_dGrid.BeginInvoke(new Action<int>(FillGrid_fromMemory), N);
			return;
		}
		List<MainForm.DataEntry> allEntriesInOrder = MainForm.LoadedData.GetAllEntriesInOrder();
		int num = Math.Max(0, allEntriesInOrder.Count - N);
		for (int i = num; i < allEntriesInOrder.Count; i++)
		{
			MainForm.DataEntry dataEntry = allEntriesInOrder[i];
			ulong dataFifoRaw = Convert.ToUInt64(dataEntry.RAW, 16);
			DaqFifoGridFill(dataFifoRaw);
			if (dataEntry.AdjCtrl_DCO0.HasValue)
			{
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[14].Value = dataEntry.AdjCtrl_DCO0;
			}
			if (dataEntry.AdjCtrl_DCO1.HasValue)
			{
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[15].Value = dataEntry.AdjCtrl_DCO1;
			}
		}
	}

	private void PLOT_ToggleFunction()
	{
		if (PLOT_ToggleStateIsCAL)
		{
			PLOT_ToggleDataType_but.BackColor = Color.Tomato;
			PLOT_ToggleCAL_label.BackColor = Color.Transparent;
			PLOT_ToggleCAL_label.ForeColor = Color.Black;
			PLOT_ToggleCAL_label.BorderStyle = BorderStyle.Fixed3D;
			PLOT_ToggleCAL_label.Enabled = true;
			PLOT_ToggleMES_label.BackColor = Color.Transparent;
			PLOT_ToggleMES_label.Enabled = false;
			PLOT_ToggleMES_label.BorderStyle = BorderStyle.None;
			PLOT_XAxis_comboBox.Items.Clear();
			PLOT_XAxis_comboBox.Items.Add("Mattonella ID");
			PLOT_XAxis_comboBox.Items.Add("Pixel ID");
			PLOT_XAxis_comboBox.Items.Add("DCO-0 Period");
			PLOT_XAxis_comboBox.Items.Add("DCO-1 Period");
			PLOT_XAxis_comboBox.Items.Add("TOT");
			PLOT_XAxis_comboBox.Items.Add("Loading Order");
			PLOT_XAxis_comboBox.Items.Add("DCO-0 Config. Code");
			PLOT_XAxis_comboBox.Items.Add("DCO-1 Config. Code");
			PLOT_XAxis_comboBox.Items.Add("All DCO Period");
			PLOT_XAxis_comboBox.SelectedIndex = 1;
			PLOT_YAxis_comboBox.Items.Clear();
			PLOT_YAxis_comboBox.Items.Add("Mattonella ID");
			PLOT_YAxis_comboBox.Items.Add("Pixel ID");
			PLOT_YAxis_comboBox.Items.Add("DCO-0 Period");
			PLOT_YAxis_comboBox.Items.Add("DCO-1 Period");
			PLOT_YAxis_comboBox.Items.Add("TOT");
			PLOT_YAxis_comboBox.Items.Add("Loading Order");
			PLOT_YAxis_comboBox.Items.Add("DCO-0 Config. Code");
			PLOT_YAxis_comboBox.Items.Add("DCO-1 Config. Code");
			PLOT_YAxis_comboBox.SelectedIndex = 0;
		}
		else
		{
			PLOT_ToggleDataType_but.BackColor = Color.LightBlue;
			PLOT_ToggleMES_label.BackColor = Color.Transparent;
			PLOT_ToggleMES_label.ForeColor = Color.Black;
			PLOT_ToggleMES_label.BorderStyle = BorderStyle.Fixed3D;
			PLOT_ToggleMES_label.Enabled = true;
			PLOT_ToggleCAL_label.BackColor = Color.Transparent;
			PLOT_ToggleCAL_label.Enabled = false;
			PLOT_ToggleCAL_label.BorderStyle = BorderStyle.None;
			PLOT_XAxis_comboBox.Items.Clear();
			PLOT_XAxis_comboBox.Items.Add("Mattonella ID");
			PLOT_XAxis_comboBox.Items.Add("Pixel ID");
			PLOT_XAxis_comboBox.Items.Add("DCO-0 Period");
			PLOT_XAxis_comboBox.Items.Add("DCO-1 Period");
			PLOT_XAxis_comboBox.Items.Add("TOT");
			PLOT_XAxis_comboBox.Items.Add("TA");
			PLOT_XAxis_comboBox.Items.Add("Loading Order");
			PLOT_XAxis_comboBox.Items.Add("DCO-0 Config. Code");
			PLOT_XAxis_comboBox.Items.Add("DCO-1 Config. Code");
			PLOT_XAxis_comboBox.Items.Add("TA difference 2CH");
			PLOT_XAxis_comboBox.Items.Add("TA Meas. - TA Exp.");
			PLOT_XAxis_comboBox.Items.Add("TA Expected");
			PLOT_XAxis_comboBox.SelectedIndex = 1;
			PLOT_YAxis_comboBox.Items.Clear();
			PLOT_YAxis_comboBox.Items.Add("Mattonella ID");
			PLOT_YAxis_comboBox.Items.Add("Pixel ID");
			PLOT_YAxis_comboBox.Items.Add("DCO-0 Period");
			PLOT_YAxis_comboBox.Items.Add("DCO-1 Period");
			PLOT_YAxis_comboBox.Items.Add("TOT");
			PLOT_YAxis_comboBox.Items.Add("TA");
			PLOT_YAxis_comboBox.Items.Add("Loading Order");
			PLOT_YAxis_comboBox.Items.Add("DCO-0 Config. Code");
			PLOT_YAxis_comboBox.Items.Add("DCO-1 Config. Code");
			PLOT_YAxis_comboBox.Items.Add("TA difference 2CH");
			PLOT_YAxis_comboBox.Items.Add("TA Meas. - TA Exp.");
			PLOT_YAxis_comboBox.Items.Add("TA Expected");
			PLOT_YAxis_comboBox.Items.Add("Sigma TA");
			PLOT_YAxis_comboBox.SelectedIndex = 0;
		}
	}

	private List<dynamic> FilterData(List<MainForm.DataEntry> OG_Data, Func<MainForm.DataEntry, dynamic> Filter)
	{
		if (OG_Data.Count == 0)
		{
			throw new ArgumentException("OG_Data empty");
		}
		List<object> list = new List<object>();
		for (int i = 0; i < OG_Data.Count; i++)
		{
			list.Add((dynamic)Filter(OG_Data[i]));
		}
		return list;
	}

	private void PLOT_ScatterPlot(FormsPlot plot, List<dynamic> DynamicDataX, List<dynamic> DynamicDataY, string title = "T_NULL", string xAxisTitle = "X_NULL", string yAxisTitle = "Y_NULL")
	{
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Expected O, but got Unknown
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		((FormsPlotBase)Variable_Plot).Plot.Remove<BarPlot>();
		((FormsPlotBase)Variable_Plot).Plot.Remove<Heatmap>();
		object[] array = DynamicDataX.ToArray();
		object[] array2 = DynamicDataY.ToArray();
		Scatter val = ((FormsPlotBase)plot).Plot.Add.ScatterPoints<object, object>(array, array2, (Color?)null);
		val.LegendText = title;
		MarkerShape[] array3 = (MarkerShape[])Enum.GetValues(typeof(MarkerShape));
		val.MarkerStyle.Shape = array3[(plot_counter * 2 + 11) % 12];
		val.MarkerStyle.Size = 8f;
		val.MarkerLineWidth = 3f;
		val.MarkerStyle.OutlineWidth = 1f;
		IPalette val2 = (IPalette)new ColorblindFriendly();
		Color color = val2.GetColor(plot_counter % 8);
		val.LineColor = ((Color)(ref color)).WithAlpha(0.75);
		val.MarkerFillColor = color;
		val.MarkerLineColor = color;
		val.MarkerStyle.OutlineColor = Colors.Black;
		((FormsPlotBase)plot).Plot.Axes.AutoScale((bool?)null, (bool?)null);
		((FormsPlotBase)plot).Plot.Title(title, (float?)26f);
		((FormsPlotBase)plot).Plot.XLabel(xAxisTitle, (float?)18f);
		((FormsPlotBase)plot).Plot.YLabel(yAxisTitle, (float?)null);
		((FormsPlotBase)plot).Plot.Legend.IsVisible = true;
		((FormsPlotBase)plot).Plot.ShowLegend();
		plot_counter++;
		double value = Statistics.Mean((IEnumerable<double>)array2.Select(Convert.ToDouble).ToArray());
		PlotAvg1_textBox.Text = Math.Round(value, 2).ToString();
		double value2 = Statistics.StandardDeviation((IEnumerable<double>)array2.Select(Convert.ToDouble).ToArray());
		PlotSigma1_textBox.Text = Math.Round(value2, 2).ToString();
		((FormsPlotBase)plot).Plot.Axes.AutoScale((bool?)null, (bool?)null);
	}

	private void PLOT_Histogram(FormsPlot plot, List<dynamic> DynamicData, int bincount, bool normalize = false, string title = "T_NULL", string xAxisTitle = "X_NULL", string yAxisTitle = "Counts")
	{
		//IL_0146: Unknown result type (might be due to invalid IL or missing references)
		//IL_014c: Expected O, but got Unknown
		//IL_0208: Unknown result type (might be due to invalid IL or missing references)
		//IL_020d: Unknown result type (might be due to invalid IL or missing references)
		//IL_021a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0227: Unknown result type (might be due to invalid IL or missing references)
		//IL_0231: Expected O, but got Unknown
		//IL_0235: Unknown result type (might be due to invalid IL or missing references)
		//IL_023a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0247: Unknown result type (might be due to invalid IL or missing references)
		((FormsPlotBase)Variable_Plot).Plot.Remove<Scatter>();
		((FormsPlotBase)Variable_Plot).Plot.Remove<Heatmap>();
		if (DynamicData.Count == 0)
		{
			MessageBox.Show("DataList empty");
			return;
		}
		List<double> list = DynamicData.Select(Convert.ToDouble).ToList();
		if (reduced_sigma)
		{
			bool flag = false;
			int num = 0;
			while (!flag && num < 10)
			{
				List<double> list2 = list;
				list = MainForm.ChauvenetFilter(list2, z_tolerance);
				if (list2.Count == list.Count)
				{
					flag = true;
				}
				num++;
			}
		}
		if (list.Count <= 1)
		{
			list.Add(list[0] * 0.95 - 0.001);
			list.Add(list[0] * 1.05 + 0.001);
			bincount = 3;
		}
		double[] array = new double[list.Count];
		for (int i = 0; i < list.Count; i++)
		{
			array[i] = Convert.ToDouble(list[i]);
		}
		if (array.Max() == array.Min())
		{
			MessageBox.Show("All entries are the same. \n Generating empty plot.");
			array[0] += array.Max() * 0.05;
			bincount = 3;
		}
		IPalette val = (IPalette)new ColorblindFriendly();
		Histogram val2 = Histogram.WithBinCount(bincount, (IEnumerable<double>)array);
		BarPlot val3 = (normalize ? ((FormsPlotBase)plot).Plot.Add.Bars((IEnumerable<double>)val2.Bins, (IEnumerable<double>)val2.GetProbability(1.0)) : ((FormsPlotBase)plot).Plot.Add.Bars<int>((IEnumerable<double>)val2.Bins, (IEnumerable<int>)val2.Counts));
		foreach (Bar bar in val3.Bars)
		{
			bar.Size = val2.FirstBinSize;
			bar.Position += val2.FirstBinSize * 0.5;
			bar.LineWidth = 0f;
			bar.FillStyle.AntiAlias = false;
			Color val4 = val.GetColor(plot_counter % 8);
			bar.FillColor = ((Color)(ref val4)).WithAlpha(0.75);
			bar.FillHatch = (IHatch)new Striped((StripeDirection)0);
			val4 = bar.FillColor;
			bar.FillHatchColor = ((Color)(ref val4)).Lighten(0.5);
		}
		((FormsPlotBase)plot).Plot.Axes.AutoScale((bool?)null, (bool?)null);
		((FormsPlotBase)plot).Plot.Title(title, (float?)null);
		((FormsPlotBase)plot).Plot.XLabel(xAxisTitle, (float?)null);
		val3.LegendText = title + $"\r\nN = {val2.Counts.Sum()}";
		if (yAxisTitle == "Counts")
		{
			((FormsPlotBase)plot).Plot.YLabel(normalize ? "Probability" : yAxisTitle, (float?)null);
		}
		else
		{
			((FormsPlotBase)plot).Plot.YLabel(yAxisTitle, (float?)null);
		}
		plot_counter++;
		((FormsPlotBase)plot).Plot.Legend.IsVisible = true;
		((FormsPlotBase)plot).Plot.ShowLegend();
		double value = Statistics.Mean((IEnumerable<double>)list.ToArray());
		PlotAvg1_textBox.Text = Math.Round(value, 2).ToString();
		double value2 = Statistics.StandardDeviation((IEnumerable<double>)list.ToArray());
		PlotSigma1_textBox.Text = Math.Round(value2, 2).ToString();
	}

	private void PLOT_HeatMap(FormsPlot plot, List<MainForm.DataEntry> DataList, Func<MainForm.DataEntry, dynamic> xValues, Func<MainForm.DataEntry, dynamic> yValues, string title = "T_NULL", string xAxisTitle = "X_NULL", string yAxisTitle = "Y_NULL")
	{
		int count = DataList.Count;
		object[] array = new object[count];
		object[] array2 = new object[count];
		for (int i = 0; i < count; i++)
		{
			array[i] = xValues(DataList[i]);
			array2[i] = yValues(DataList[i]);
		}
		Scatter val = ((FormsPlotBase)plot).Plot.Add.ScatterPoints<object, object>(array, array2, (Color?)null);
		val.LegendText = title;
		val.MarkerStyle.Shape = (MarkerShape)11;
		((FormsPlotBase)plot).Plot.Axes.AutoScale((bool?)null, (bool?)null);
		((FormsPlotBase)plot).Plot.Title(title, (float?)null);
		((FormsPlotBase)plot).Plot.XLabel(xAxisTitle, (float?)null);
		((FormsPlotBase)plot).Plot.YLabel(yAxisTitle, (float?)null);
		((FormsPlotBase)plot).Plot.Legend.IsVisible = true;
		((FormsPlotBase)plot).Plot.ShowLegend();
		plot_counter++;
	}

	private void PLOT_PlotCAL(int? MAT_ID = null, int? PIX_ID = null)
	{
		ushort mat = Convert.ToUInt16(PLOT_MatSelect_UpDown.Value);
		ushort pix = Convert.ToUInt16(PLOT_PixSelect_UpDown.Value);
		bool flag = PLOT_MatSelectALL_chkBox.Checked;
		bool flag2 = PLOT_PixSelectALL_chkBox.Checked;
		if (flag)
		{
			mat = ushort.MaxValue;
		}
		if (flag2)
		{
			pix = ushort.MaxValue;
		}
		List<MainForm.DataEntry> allEntriesInOrder = MainForm.LoadedData.GetAllEntriesInOrder();
		MainForm.StructuredKey key_To_Check = new MainForm.StructuredKey(mat, pix, 1, ushort.MaxValue);
		List<MainForm.DataEntry> list = new List<MainForm.DataEntry>();
		MainForm.StructuredKey key_To_Check2 = new MainForm.StructuredKey(mat, pix, 1, 0);
		List<MainForm.DataEntry> list2 = new List<MainForm.DataEntry>();
		MainForm.StructuredKey key_To_Check3 = new MainForm.StructuredKey(mat, pix, 1, (ushort)1);
		List<MainForm.DataEntry> list3 = new List<MainForm.DataEntry>();
		foreach (MainForm.DataEntry item in allEntriesInOrder)
		{
			MainForm.StructuredKey structuredKey = new MainForm.StructuredKey(item.MAT, item.PIX, item.CAL_Mode, item.DCO);
			if (structuredKey.Find_Any_ushortMaxValue(key_To_Check))
			{
				list.Add(item);
			}
			if (structuredKey.Find_Any_ushortMaxValue(key_To_Check2))
			{
				list2.Add(item);
			}
			if (structuredKey.Find_Any_ushortMaxValue(key_To_Check3))
			{
				list3.Add(item);
			}
		}
		List<object> list4 = new List<object>();
		List<object> list5 = new List<object>();
		if (MainForm.LoadedData == null)
		{
			MessageBox.Show("Error: Loaded Data is null. Please load data.", "Null Data Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			return;
		}
		if (MainForm.LoadedData.calibra_counter == 0)
		{
			MessageBox.Show("Error: Selected Data is null. No Calibration data found.", "Null Data Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			return;
		}
		string text;
		switch (PLOT_XAxis_comboBox.SelectedIndex)
		{
		case 0:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.MAT;
			text = "Mattonella";
			list4 = FilterData(list, filter);
			break;
		}
		case 1:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.PIX;
			text = "Pixel";
			list4 = FilterData(list, filter);
			break;
		}
		case 2:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.DCO0_T_picoS;
			text = "DCO 0 Period [ps]";
			list4 = FilterData(list2, filter);
			break;
		}
		case 3:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.DCO1_T_picoS;
			text = "DCO 1 Period [ps]";
			list4 = FilterData(list3, filter);
			break;
		}
		case 4:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.TOT_picoS;
			text = "TOT [ps]";
			list4 = FilterData(list, filter);
			break;
		}
		case 5:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.Order;
			text = "Loading Order";
			list4 = FilterData(list, filter);
			break;
		}
		case 6:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.AdjCtrl_DCO0;
			text = "DCO 0 Configuration Code";
			list4 = FilterData(list2, filter);
			break;
		}
		case 7:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.AdjCtrl_DCO1;
			text = "DCO 1 Configuration Code";
			list4 = FilterData(list3, filter);
			break;
		}
		case 8:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.DCO0_T_picoS;
			list4 = FilterData(list2, filter);
			filter = (MainForm.DataEntry entry) => (dynamic)entry.DCO1_T_picoS;
			foreach (dynamic item2 in FilterData(list3, filter))
			{
				list4.Add(item2);
			}
			text = "All DCO Period [ps]";
			break;
		}
		default:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.PIX;
			text = "Pixel";
			list4 = FilterData(list, filter);
			break;
		}
		}
		string text2;
		switch (PLOT_YAxis_comboBox.SelectedIndex)
		{
		case 0:
		{
			Func<MainForm.DataEntry, object> filter2 = (MainForm.DataEntry entry) => (dynamic)entry.MAT;
			text2 = "Mattonella";
			list5 = FilterData(list, filter2);
			break;
		}
		case 1:
		{
			Func<MainForm.DataEntry, object> filter2 = (MainForm.DataEntry entry) => (dynamic)entry.PIX;
			text2 = "Pixel";
			list5 = FilterData(list, filter2);
			break;
		}
		case 2:
		{
			Func<MainForm.DataEntry, object> filter2 = (MainForm.DataEntry entry) => (dynamic)entry.DCO0_T_picoS;
			text2 = "DCO 0 Period [ns]";
			list5 = FilterData(list2, filter2);
			break;
		}
		case 3:
		{
			Func<MainForm.DataEntry, object> filter2 = (MainForm.DataEntry entry) => (dynamic)entry.DCO1_T_picoS;
			text2 = "DCO 1 Period [ns]";
			list5 = FilterData(list3, filter2);
			break;
		}
		case 4:
		{
			Func<MainForm.DataEntry, object> filter2 = (MainForm.DataEntry entry) => (dynamic)entry.TOT_picoS;
			text2 = "TOT [ps]";
			list5 = FilterData(list, filter2);
			break;
		}
		case 5:
		{
			Func<MainForm.DataEntry, object> filter2 = (MainForm.DataEntry entry) => (dynamic)entry.Order;
			text2 = "Loading Order";
			list5 = FilterData(list, filter2);
			break;
		}
		case 6:
		{
			Func<MainForm.DataEntry, object> filter2 = (MainForm.DataEntry entry) => (dynamic)entry.AdjCtrl_DCO0;
			text2 = "DCO 0 Configuration Code";
			list5 = FilterData(list2, filter2);
			break;
		}
		case 7:
		{
			Func<MainForm.DataEntry, object> filter2 = (MainForm.DataEntry entry) => (dynamic)entry.AdjCtrl_DCO1;
			text2 = "DCO 1 Configuration Code";
			list5 = FilterData(list3, filter2);
			break;
		}
		case 8:
		{
			Func<MainForm.DataEntry, object> filter2 = (MainForm.DataEntry entry) => (dynamic)entry.DCO0_T_picoS;
			list5 = FilterData(list2, filter2);
			filter2 = (MainForm.DataEntry entry) => (dynamic)entry.DCO1_T_picoS;
			foreach (dynamic item3 in FilterData(list3, filter2))
			{
				list5.Add(item3);
			}
			text2 = "All DCO Period [ps]";
			break;
		}
		default:
		{
			Func<MainForm.DataEntry, object> filter2 = (MainForm.DataEntry entry) => (dynamic)entry.MAT;
			text2 = "Mattonella";
			list5 = FilterData(list, filter2);
			break;
		}
		}
		switch (PLOT_PlotType_comboBox.SelectedIndex)
		{
		case 0:
			PLOT_ScatterPlot(Variable_Plot, list4, list5, text + " vs " + text2, text, text2);
			break;
		case 1:
			PLOT_Histogram(Variable_Plot, list4, Convert.ToInt32(PLOT_BinCount_UpDown.Value), PLOT_HistNormalize_chkBox.Checked, text + " Distribution", text);
			break;
		}
		((Control)(object)Variable_Plot).Refresh();
	}

	private void PLOT_PlotMES(int? MAT_ID = null, int? PIX_ID = null)
	{
		List<MainForm.DataEntry> list = new List<MainForm.DataEntry>();
		List<MainForm.DataEntry> list2 = new List<MainForm.DataEntry>();
		List<object> list3 = new List<object>();
		List<object> list4 = new List<object>();
		ushort mat = Convert.ToUInt16(PLOT_MatSelect_UpDown.Value);
		ushort num = Convert.ToUInt16(PLOT_PixSelect_UpDown.Value);
		ushort num2 = Convert.ToUInt16(PLOT_PixSelect2_UpDown.Value);
		bool flag = PLOT_MatSelectALL_chkBox.Checked;
		bool flag2 = PLOT_PixSelectALL_chkBox.Checked;
		if (flag)
		{
			mat = ushort.MaxValue;
		}
		if (flag2)
		{
			num = ushort.MaxValue;
		}
		if (MainForm.LoadedData.measure_counter == 0)
		{
			MessageBox.Show("Error: Loaded Data is empty. Please load data.", "Null Data Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			return;
		}
		if (MainForm.LoadedData.GetByMAT_PIX(mat, num) == null)
		{
			MessageBox.Show("Error: Selected Data is null. No data for selected MAT/PIX combination.", "Null Data Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			return;
		}
		MainForm.StructuredKey key_To_Check = new MainForm.StructuredKey(mat, num, 0, null);
		MainForm.StructuredKey key_To_Check2 = new MainForm.StructuredKey(mat, num2, 0, null);
		List<MainForm.DataEntry> allEntriesInOrder = MainForm.LoadedData.GetAllEntriesInOrder();
		foreach (MainForm.DataEntry item in allEntriesInOrder)
		{
			MainForm.StructuredKey structuredKey = new MainForm.StructuredKey(item.MAT, item.PIX, item.CAL_Mode, item.DCO);
			if (structuredKey.Find_Any_ushortMaxValue(key_To_Check))
			{
				list.Add(item);
			}
			if (structuredKey.Find_Any_ushortMaxValue(key_To_Check2))
			{
				list2.Add(item);
			}
		}
		Func<MainForm.DataEntry, object> func;
		string text;
		switch (PLOT_XAxis_comboBox.SelectedIndex)
		{
		case 0:
			func = (MainForm.DataEntry entry) => (dynamic)entry.MAT;
			text = "Mattonella";
			list3 = FilterData(list, func);
			break;
		case 1:
			func = (MainForm.DataEntry entry) => (dynamic)entry.PIX;
			text = "Pixel";
			list3 = FilterData(list, func);
			break;
		case 2:
			func = (MainForm.DataEntry entry) => (dynamic)entry.DCO0_T_picoS;
			text = "DCO 0 Period [ps]";
			list3 = FilterData(list, func);
			break;
		case 3:
			func = (MainForm.DataEntry entry) => (dynamic)entry.DCO1_T_picoS;
			text = "DCO 1 Period [ps]";
			list3 = FilterData(list, func);
			break;
		case 4:
			func = (MainForm.DataEntry entry) => (dynamic)entry.TOT_picoS;
			text = "TOT [ps]";
			list3 = FilterData(list, func);
			break;
		case 5:
			func = (MainForm.DataEntry entry) => (dynamic)entry.TA_picoS;
			text = "TA [ps]";
			list3 = FilterData(list, func);
			break;
		case 6:
			func = (MainForm.DataEntry entry) => (dynamic)entry.Order;
			text = "Loading Order";
			list3 = FilterData(list, func);
			break;
		case 7:
			func = (MainForm.DataEntry entry) => (dynamic)entry.AdjCtrl_DCO0;
			text = "DCO 0 Configuration Code";
			list3 = FilterData(list, func);
			break;
		case 8:
			func = (MainForm.DataEntry entry) => (dynamic)entry.AdjCtrl_DCO1;
			text = "DCO 1 Configuration Code";
			list3 = FilterData(list, func);
			break;
		case 9:
		{
			func = (MainForm.DataEntry entry) => (dynamic)entry.TA_picoS;
			(from MainForm.DataEntry e in list
				orderby e.Order
				select e).ToList();
			(from MainForm.DataEntry e in list2
				orderby e.Order
				select e).ToList();
			Dictionary<ushort?, List<MainForm.DataEntry>> dictionary = (from e in list2
				group e by e.TimeStamp).ToDictionary((IGrouping<ushort?, MainForm.DataEntry> g) => g.Key, (IGrouping<ushort?, MainForm.DataEntry> g) => g.OrderBy((MainForm.DataEntry e) => e.Order).ToList());
			list3.Clear();
			foreach (MainForm.DataEntry item2 in list)
			{
				ushort? timeStamp = item2.TimeStamp;
				double num3 = item2.TA_picoS.Value;
				double num4 = 0.0;
				List<MainForm.DataEntry> value = new List<MainForm.DataEntry>();
				if (timeStamp.HasValue)
				{
					ushort value2 = timeStamp.Value;
					ushort value3 = (ushort)((value2 == 0) ? 511u : ((uint)(value2 - 1)));
					ushort value4 = (ushort)((value2 != 511) ? ((uint)(value2 + 1)) : 0u);
					if (!dictionary.TryGetValue(value2, out value) && !dictionary.TryGetValue(value4, out value) && !dictionary.TryGetValue(value3, out value))
					{
						continue;
					}
				}
				int order = item2.Order;
				ushort mAT = item2.MAT;
				MainForm.DataEntry dataEntry = null;
				for (int num5 = value.Count - 1; num5 >= 0; num5--)
				{
					int valueOrDefault = item2.TimeStamp.GetValueOrDefault();
					int num6 = value[num5].TimeStamp ?? 1000;
					int num7 = Math.Abs(num6 - valueOrDefault);
					if (num7 == 1 && Math.Abs(value[num5].Order - order) <= 1 && value[num5].MAT == mAT)
					{
						if (valueOrDefault > num6)
						{
							num3 = Math.Abs(num3 - 25000.0);
						}
						else
						{
							num4 = Math.Abs(dataEntry.TA_picoS.Value - 25000.0);
						}
						break;
					}
					if (Math.Abs(value[num5].Order - order) <= 1 && value[num5].MAT == mAT)
					{
						dataEntry = value[num5];
						num4 = dataEntry.TA_picoS.Value;
						break;
					}
				}
				if (dataEntry != null || dataEntry != null)
				{
					list3.Add(num3 - num4);
				}
			}
			text = $"TA Difference (Pix{num} - Pix{num2}) [ps]";
			break;
		}
		case 10:
		{
			func = (MainForm.DataEntry entry) => (dynamic)entry.TA_Code;
			text = "TA Measured - TA Expected [ps]";
			bool flag4 = true;
			if (Convert.ToUInt16(PLOT_TAcode_UpDown.Value) != 0)
			{
				flag4 = false;
			}
			foreach (MainForm.DataEntry item3 in list)
			{
				ushort? tA_Code2 = item3.TA_Code;
				if (tA_Code2.HasValue && (flag4 || tA_Code2.Value == Convert.ToUInt16(PLOT_TAcode_UpDown.Value)))
				{
					double num9 = 25000.0 - (double)(int)tA_Code2.Value * 1562.5;
					list3.Add(item3.TA_picoS - num9);
				}
			}
			break;
		}
		case 11:
		{
			func = (MainForm.DataEntry entry) => (dynamic)entry.TA_Code;
			text = "TA Expected [ps]";
			bool flag3 = true;
			if (Convert.ToUInt16(PLOT_TAcode_UpDown.Value) != 0)
			{
				flag3 = false;
			}
			foreach (MainForm.DataEntry item4 in list)
			{
				ushort? tA_Code = item4.TA_Code;
				if (tA_Code.HasValue && (flag3 || tA_Code.Value == Convert.ToUInt16(PLOT_TAcode_UpDown.Value)))
				{
					double num8 = 25000.0 - (double)(int)tA_Code.Value * 1562.5;
					list3.Add(num8);
				}
			}
			break;
		}
		default:
			func = (MainForm.DataEntry entry) => (dynamic)entry.PIX;
			text = "Pixel";
			list3 = FilterData(list, func);
			break;
		}
		string text2;
		switch (PLOT_YAxis_comboBox.SelectedIndex)
		{
		case 0:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.MAT;
			text2 = "Mattonella";
			list4 = FilterData(list, filter);
			break;
		}
		case 1:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.PIX;
			text2 = "Pixel";
			list4 = FilterData(list, filter);
			break;
		}
		case 2:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.DCO0_T_picoS;
			text2 = "DCO 0 Period [ps]";
			list4 = FilterData(list, filter);
			break;
		}
		case 3:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.DCO1_T_picoS;
			text2 = "DCO 1 Period [ps]";
			list4 = FilterData(list, filter);
			break;
		}
		case 4:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.TOT_picoS;
			text2 = "TOT [ps]";
			list4 = FilterData(list, filter);
			break;
		}
		case 5:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.TA_picoS;
			text2 = "TA [ps]";
			list4 = FilterData(list, filter);
			break;
		}
		case 6:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.Order;
			text2 = "Loading Order";
			list4 = FilterData(list, filter);
			break;
		}
		case 7:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.AdjCtrl_DCO0;
			text2 = "DCO 0 Configuration Code";
			list4 = FilterData(list, filter);
			break;
		}
		case 8:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.AdjCtrl_DCO1;
			text2 = "DCO 1 Configuration Code";
			list4 = FilterData(list, filter);
			break;
		}
		case 9:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.TA_picoS;
			(from MainForm.DataEntry e in list
				orderby e.Order
				select e).ToList();
			(from MainForm.DataEntry e in list2
				orderby e.Order
				select e).ToList();
			Dictionary<ushort?, List<MainForm.DataEntry>> dictionary2 = (from e in list2
				group e by e.TimeStamp).ToDictionary((IGrouping<ushort?, MainForm.DataEntry> g) => g.Key, (IGrouping<ushort?, MainForm.DataEntry> g) => g.OrderBy((MainForm.DataEntry e) => e.Order).ToList());
			list4.Clear();
			foreach (MainForm.DataEntry item5 in list)
			{
				ushort? timeStamp2 = item5.TimeStamp;
				double num10 = item5.TA_picoS.Value;
				double num11 = 0.0;
				List<MainForm.DataEntry> value5 = new List<MainForm.DataEntry>();
				if (timeStamp2.HasValue)
				{
					ushort value6 = timeStamp2.Value;
					ushort value7 = (ushort)((value6 == 0) ? 511u : ((uint)(value6 - 1)));
					ushort value8 = (ushort)((value6 != 511) ? ((uint)(value6 + 1)) : 0u);
					if (!dictionary2.TryGetValue(value6, out value5) && !dictionary2.TryGetValue(value8, out value5) && !dictionary2.TryGetValue(value7, out value5))
					{
						continue;
					}
				}
				int order2 = item5.Order;
				ushort mAT2 = item5.MAT;
				MainForm.DataEntry dataEntry2 = null;
				for (int num12 = value5.Count - 1; num12 >= 0; num12--)
				{
					int num13 = ((int?)item5.TimeStamp) ?? (-50);
					int num14 = value5[num12].TimeStamp ?? 1000;
					int num15 = Math.Abs(num14 - num13);
					if (num15 == 1 && Math.Abs(value5[num12].Order - order2) <= 1 && value5[num12].MAT == mAT2)
					{
						if (num13 > num14)
						{
							num10 = Math.Abs(num10 - 25000.0);
						}
						else
						{
							num11 = Math.Abs(dataEntry2.TA_picoS.Value - 25000.0);
						}
						break;
					}
					if (Math.Abs(value5[num12].Order - order2) <= 1 && value5[num12].MAT == mAT2)
					{
						dataEntry2 = value5[num12];
						num11 = dataEntry2.TA_picoS.Value;
						break;
					}
				}
				if (dataEntry2 != null || dataEntry2 != null)
				{
					list4.Add(num10 - num11);
				}
			}
			text2 = $"TA Difference (Pix{num} - Pix{num2}) [ps]";
			break;
		}
		case 10:
		{
			text2 = "TA Measured - TA Expected [ps]";
			bool flag6 = true;
			if (Convert.ToUInt16(PLOT_TAcode_UpDown.Value) != 0)
			{
				flag6 = false;
			}
			foreach (MainForm.DataEntry item6 in list)
			{
				ushort? tA_Code4 = item6.TA_Code;
				if (tA_Code4.HasValue && (flag6 || tA_Code4.Value == Convert.ToUInt16(PLOT_TAcode_UpDown.Value)))
				{
					double num18 = 25000.0 - (double)(int)tA_Code4.Value * 1562.5;
					list4.Add(item6.TA_picoS - num18);
				}
			}
			break;
		}
		case 11:
		{
			text2 = "TA Expected [ps]";
			bool flag5 = true;
			if (Convert.ToUInt16(PLOT_TAcode_UpDown.Value) != 0)
			{
				flag5 = false;
			}
			foreach (MainForm.DataEntry item7 in list)
			{
				ushort? tA_Code3 = item7.TA_Code;
				if (tA_Code3.HasValue && (flag5 || tA_Code3.Value == Convert.ToUInt16(PLOT_TAcode_UpDown.Value)))
				{
					double num17 = 25000.0 - (double)(int)tA_Code3.Value * 1562.5;
					list4.Add(num17);
				}
			}
			break;
		}
		case 12:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.TA_picoS;
			text2 = "sigmaTA [ps]";
			Dictionary<object, List<double>> dictionary3 = new Dictionary<object, List<double>>();
			Dictionary<object, List<double>> dictionary4 = new Dictionary<object, List<double>>();
			foreach (MainForm.DataEntry item8 in list)
			{
				dynamic val = func(item8);
				if (val != null)
				{
					if ((!dictionary3.ContainsKey(val)))
					{
						dictionary3[val] = new List<double>();
					}
					if (item8.TA_picoS.HasValue)
					{
						dictionary3[val].Add(item8.TA_picoS.Value);
					}
					if ((!dictionary4.ContainsKey(val)))
					{
						dictionary4[val] = new List<double>();
					}
					dictionary4[val].Add(val);
				}
			}
			list3.Clear();
			foreach (KeyValuePair<object, List<double>> item9 in dictionary3)
			{
				double num16 = Statistics.StandardDeviation((IEnumerable<double>)item9.Value);
				list4.Add(num16);
				list3.Add((dynamic)item9.Key);
			}
			list4 = list4.Distinct().ToList();
			list3 = list3.Distinct().ToList();
			break;
		}
		default:
		{
			Func<MainForm.DataEntry, object> filter = (MainForm.DataEntry entry) => (dynamic)entry.MAT;
			text2 = "Mattonella";
			list4 = FilterData(list, filter);
			break;
		}
		}
		switch (PLOT_PlotType_comboBox.SelectedIndex)
		{
		case 0:
			PLOT_ScatterPlot(Variable_Plot, list3, list4, text + " vs " + text2, text, text2);
			break;
		case 1:
			PLOT_Histogram(Variable_Plot, list3, Convert.ToInt32(PLOT_BinCount_UpDown.Value), PLOT_HistNormalize_chkBox.Checked, text + " Distribution", text);
			break;
		}
		((Control)(object)Variable_Plot).Refresh();
	}

	private void ReadSingleFifo_but_Click(object sender, EventArgs e)
	{
		ulong num = MyMain.FifoReadSingle();
		MainForm.LoadedData.AddDataFromRaw(num, MyMain);
		if (num != 0L)
		{
			DaqFifoGridFill(num);
			ShowLastVisibleRows();
		}
	}

	private async void ReadNWordsFifo_but_Click(object sender, EventArgs e)
	{
		await FifoReadNum(Convert.ToInt32(FifoNWord_UpDown.Value));
		ShowLastVisibleRows();
	}

	private async void StartReadingButton_Click(object sender, EventArgs e)
	{
		ToggleControlsEnabledState(enable: false);
		ReadingSTOP_but.Enabled = true;
		STOP_Token_Source = new CancellationTokenSource();
		CancellationToken STOP_Token = STOP_Token_Source.Token;
		try
		{
			await Task.Run(() => ReadContinuouslyAsync(STOP_Token));
			ShowLastVisibleRows();
		}
		catch (OperationCanceledException)
		{
			MessageBox.Show("Reading stopped.");
		}
		ToggleControlsEnabledState(enable: true);
	}

	private async void StartReadingToFileButton_Click(object sender, EventArgs e)
	{
		using SaveFileDialog saveFileDialog = new SaveFileDialog();
		saveFileDialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
		saveFileDialog.Title = "Save Data File";
		if (saveFileDialog.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		ToggleControlsEnabledState(enable: false);
		ReadingSTOP_but.Enabled = true;
		STOP_Token_Source = new CancellationTokenSource();
		CancellationToken token = STOP_Token_Source.Token;
		try
		{
			using StreamWriter writer = new StreamWriter(saveFileDialog.FileName, append: true);
			await ReadContinuouslyToFileAsync(writer, token);
		}
		catch (OperationCanceledException)
		{
			MessageBox.Show("Reading stopped.");
			ToggleControlsEnabledState(enable: true);
		}
		catch (Exception ex2)
		{
			MessageBox.Show("An error occurred: " + ex2.Message);
			ToggleControlsEnabledState(enable: true);
		}
		ToggleControlsEnabledState(enable: true);
	}

	private void StopReadingButton_Click(object sender, EventArgs e)
	{
		CancelStopToken();
	}

	private async void FifoReadUntilEmpty_but_Click(object sender, EventArgs e)
	{
		ToggleControlsEnabledState(enable: false);
		DialogResult dialogResult = MessageBox.Show("Want to fill grid aswell?", "Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
		if (dialogResult == DialogResult.Yes)
		{
			FillGrid_fromMemory(await ReadUntilEmpty(gridlimit: true));
		}
		else if (await ReadUntilEmpty(gridlimit: true) > 1023)
		{
			MessageBox.Show("Read 1024 events, FIFO still not empty");
		}
		ShowLastVisibleRows();
		ToggleControlsEnabledState(enable: true);
	}

	private void PlotToggleTypeButton_Click(object sender, EventArgs e)
	{
		PLOT_ToggleStateIsCAL = !PLOT_ToggleStateIsCAL;
		PLOT_ToggleFunction();
	}

	private void PlotType_ComboBox_IndexChanged(object sender, EventArgs e)
	{
		ModularShowUI(sender, e);
	}

	private void XAxis_ComboBox_IndexChanged(object sender, EventArgs e)
	{
		ModularShowUI(sender, e);
	}

	private void PlotGenerateButton_Click(object sender, EventArgs e)
	{
		if (PLOT_RemoveOutliers_chkBox.Checked)
		{
			reduced_sigma = true;
			z_tolerance = Convert.ToDouble(PLOT_RemoveOutliers_numUpDown.Value);
		}
		else
		{
			reduced_sigma = false;
		}
		if (PLOT_ToggleStateIsCAL)
		{
			PLOT_PlotCAL();
		}
		if (!PLOT_ToggleStateIsCAL)
		{
			PLOT_PlotMES();
		}
		Plot_Panel.Visible = true;
		PLOT_EnhancePlotStyle();
	}

	private void PlotClearButton_Click(object sender, EventArgs e)
	{
		((FormsPlotBase)Variable_Plot).Plot.Clear();
		plot_counter = 0;
		Plot_Panel.Visible = false;
	}

	private async void btnSaveWholeDataToFile_Click(object sender, EventArgs e)
	{
		if (FIFO_saveFileDialog.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		int[] TextColumnWidths = new int[25]
		{
			16, 11, 8, 3, 3, 3, 5, 10, 3, 2,
			8, 5, 5, 7, 9, 9, 10, 10, 10, 10,
			10, 10, 10, 10, 10
		};
		string filename = Path.ChangeExtension(FIFO_saveFileDialog.FileName, ".txt");
		string savefile = await Task.Run(() => MyMain.Write_LastNdata_ToString(int.MaxValue, 999, 999, MainForm.Cur_Quad));
		using (StreamWriter writer = new StreamWriter(filename, append: true))
		{
			string[] HeaderTitles = new string[25]
			{
				"RAW_DATA", "Fifo Status", "NUM FIFO", "MAT", "PIX", "TPM", "Calib", "Time stamp", "DCO", "DE",
				"CAL Time", "Cnt_1", "Cnt_0", "Cnt TOT", "DCO0 Code", "DCO1 Code", "Quad", "DCO0_T", "DCO1_T", "TA_expect",
				"TA_measured", "TOT_expect", "TOT_measured", "TA_Code", "TOT_Code"
			};
			for (int i = 0; i < HeaderTitles.Length; i++)
			{
				await writer.WriteAsync(HeaderTitles[i].ToString().PadRight(TextColumnWidths[i] + 4) + "\t");
			}
			await writer.WriteLineAsync();
			await writer.WriteAsync(savefile);
			await writer.WriteLineAsync();
		}
		MessageBox.Show("Data saved");
	}

	private void saveToolStripMenuItem_Click(object sender, EventArgs e)
	{
		FIFO_saveFileDialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
		FIFO_saveFileDialog.DefaultExt = "txt";
		string fileName = "DATA_FIFO_IGNITE64" + DateTime.Now.ToString("_yy.MM.dd.HH.mm.ss") + ".txt";
		FIFO_saveFileDialog.FileName = fileName;
		if (FIFO_saveFileDialog.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		using StreamWriter streamWriter = new StreamWriter(FIFO_saveFileDialog.FileName);
		int[] array = new int[DaqFifo_dGrid.Columns.Count];
		for (int i = 0; i < DaqFifo_dGrid.Columns.Count; i++)
		{
			array[i] = DaqFifo_dGrid.Columns[i].HeaderText.Length;
		}
		foreach (DataGridViewRow item in (IEnumerable)DaqFifo_dGrid.Rows)
		{
			if (item.IsNewRow)
			{
				continue;
			}
			for (int j = 0; j < DaqFifo_dGrid.Columns.Count; j++)
			{
				int num = item.Cells[j].Value?.ToString().Length ?? 0;
				if (num > array[j])
				{
					array[j] = num;
				}
			}
		}
		for (int k = 0; k < DaqFifo_dGrid.Columns.Count; k++)
		{
			streamWriter.Write(DaqFifo_dGrid.Columns[k].HeaderText.PadRight(array[k] + 2));
		}
		streamWriter.WriteLine();
		foreach (DataGridViewRow item2 in (IEnumerable)DaqFifo_dGrid.Rows)
		{
			if (!item2.IsNewRow)
			{
				for (int l = 0; l < DaqFifo_dGrid.Columns.Count; l++)
				{
					streamWriter.Write(item2.Cells[l].Value?.ToString().PadRight(array[l] + 2));
				}
				streamWriter.WriteLine();
			}
		}
		streamWriter.Close();
	}

	private void btnLoadDataHidden_Click(object sender, EventArgs e)
	{
		MainForm.LoadedData.LoadDataFromFile();
		string text = "Successfully loaded:\n" + $"{MainForm.LoadedData.calibra_counter}  Calibration Entries\n" + $"{MainForm.LoadedData.measure_counter}  Measurement Entries\n" + $"{MainForm.LoadedData.useless_counter}  Useless Entries";
		MessageBox.Show(text, "Load Status", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void btnLoadDataToGrid_Click(object sender, EventArgs e)
	{
		MainForm.LoadedData.LoadDataFromFile();
		List<MainForm.DataEntry> allEntriesInOrder = MainForm.LoadedData.GetAllEntriesInOrder();
		foreach (MainForm.DataEntry item in allEntriesInOrder)
		{
			DaqFifoGridFill(Convert.ToUInt64(item.RAW, 16));
			if (item.AdjCtrl_DCO0.HasValue || item.AdjCtrl_DCO1.HasValue)
			{
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[14].Value = item.AdjCtrl_DCO0;
				DaqFifo_dGrid.Rows[DaqFifo_dGrid.RowCount - 1].Cells[15].Value = item.AdjCtrl_DCO1;
			}
		}
		string text = "Successfully loaded:\n" + $"{MainForm.LoadedData.calibra_counter}  Calibration Entries\n" + $"{MainForm.LoadedData.measure_counter}  Measurement Entries\n" + $"{MainForm.LoadedData.useless_counter}  Useless Entries";
		MessageBox.Show(text, "Load Status", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void btnClearGrid_Click(object sender, EventArgs e)
	{
		DaqFifo_dGrid.Rows.Clear();
		MessageBox.Show(" Grid Successfully Deleted. \n\n Data is preserved while the program is open.", "Delete Status", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void btnClearData_Click(object sender, EventArgs e)
	{
		MainForm.LoadedData.ClearAllData();
		DaqFifo_dGrid.Rows.Clear();
		MessageBox.Show(" Data Successfully Deleted. \n\n Calibration Matrix is preserved.", "Delete Status", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void Initialize_UI_Components()
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Expected O, but got Unknown
		Plot_Panel.Controls.Add((Control)(object)Variable_Plot);
		((FormsPlotBase)Variable_Plot).Plot.Add.Palette = (IPalette)new ColorblindFriendly();
		Plot_Panel.Visible = false;
		PLOT_ToggleCAL_label.BorderStyle = BorderStyle.Fixed3D;
		PLOT_ToggleMES_label.Enabled = false;
		PLOT_PlotType_comboBox.SelectedIndex = 0;
		PLOT_PixSelect2_UpDown.Enabled = false;
		PLOT_PixSelect2_UpDown.Visible = false;
		PLOT_PixSelect2_label.Visible = false;
		PLOT_BinCount_UpDown.Value = 100m;
		PLOT_PlotType_comboBox.Items.Remove("Heatmap Maybe?");
		PLOT_ToggleFunction();
	}

	private void ModularShowUI(object sender, EventArgs e)
	{
		if (sender == PLOT_PlotType_comboBox)
		{
			PLOT_XAxis_comboBox.Enabled = true;
			if (PLOT_PlotType_comboBox.SelectedIndex != 1)
			{
				PLOT_YAxis_comboBox.Enabled = true;
				PLOT_BinCount_UpDown.Enabled = false;
				PLOT_HistNormalize_chkBox.Enabled = false;
			}
			if (PLOT_PlotType_comboBox.SelectedIndex == 1)
			{
				PLOT_BinCount_UpDown.Enabled = true;
				PLOT_HistNormalize_chkBox.Enabled = true;
			}
		}
		if (sender == PLOT_XAxis_comboBox || sender == PLOT_YAxis_comboBox || sender == PLOT_PlotType_comboBox)
		{
			PLOT_MatSelect_UpDown.Enabled = true;
			PLOT_MatSelectALL_chkBox.Enabled = true;
			PLOT_PixSelect_UpDown.Enabled = true;
			PLOT_PixSelectALL_chkBox.Enabled = true;
			if (PLOT_XAxis_comboBox.SelectedIndex == 0 || (PLOT_YAxis_comboBox.SelectedIndex == 0 && PLOT_PlotType_comboBox.SelectedIndex != 1))
			{
				PLOT_MatSelect_UpDown.Enabled = false;
				PLOT_MatSelectALL_chkBox.Enabled = false;
				PLOT_MatSelectALL_chkBox.Checked = true;
			}
			else
			{
				PLOT_MatSelect_UpDown.Enabled = true;
				PLOT_MatSelectALL_chkBox.Enabled = true;
			}
			if (PLOT_XAxis_comboBox.SelectedIndex == 1 || (PLOT_YAxis_comboBox.SelectedIndex == 1 && PLOT_PlotType_comboBox.SelectedIndex != 1))
			{
				PLOT_PixSelect_UpDown.Enabled = false;
				PLOT_PixSelectALL_chkBox.Enabled = false;
				PLOT_PixSelectALL_chkBox.Checked = true;
			}
			else
			{
				PLOT_PixSelect_UpDown.Enabled = true;
				PLOT_PixSelectALL_chkBox.Enabled = true;
			}
			if (PLOT_XAxis_comboBox.SelectedIndex == 9 || (PLOT_YAxis_comboBox.SelectedIndex == 9 && PLOT_PlotType_comboBox.SelectedIndex != 1))
			{
				PLOT_PixSelectALL_chkBox.Visible = false;
				PLOT_PixSelectALL_chkBox.Enabled = false;
				PLOT_PixSelectALL_chkBox.Checked = false;
				PLOT_PixSelect2_UpDown.Enabled = true;
				PLOT_PixSelect2_UpDown.Visible = true;
				PLOT_PixSelect2_label.Visible = true;
			}
			else
			{
				PLOT_PixSelectALL_chkBox.Visible = true;
				PLOT_PixSelectALL_chkBox.Enabled = true;
				PLOT_PixSelect2_UpDown.Enabled = false;
				PLOT_PixSelect2_UpDown.Visible = false;
				PLOT_PixSelect2_label.Visible = false;
				PLOT_TAcode_UpDown.Visible = false;
				PLOT_TAcode_label.Visible = false;
			}
			if (PLOT_XAxis_comboBox.SelectedIndex == 10 || (PLOT_YAxis_comboBox.SelectedIndex == 10 && PLOT_PlotType_comboBox.SelectedIndex != 1))
			{
				PLOT_TAcode_UpDown.Enabled = true;
				PLOT_TAcode_UpDown.Visible = true;
				PLOT_TAcode_label.Visible = true;
			}
			else
			{
				PLOT_TAcode_UpDown.Visible = false;
				PLOT_TAcode_label.Visible = false;
			}
		}
	}

	private void ToggleControlsEnabledState(bool enable)
	{
		SetEnabledRecursive(this, enable);
		if (this != null)
		{
			ReadingSTOP_but.Enabled = true;
			MyMain.MAT_Test_Routines_groupBox.Enabled = enable;
		}
	}

	private void SetEnabledRecursive(Control parent, bool enable)
	{
		foreach (Control control in parent.Controls)
		{
			control.Enabled = enable;
			if (control.HasChildren)
			{
				SetEnabledRecursive(control, enable);
			}
		}
	}

	private void ShowLastVisibleRows()
	{
		if (DaqFifo_dGrid.InvokeRequired)
		{
			DaqFifo_dGrid.BeginInvoke(new Action(ShowLastVisibleRows));
		}
		else if (DaqFifo_dGrid.Rows.Count != 0)
		{
			int count = DaqFifo_dGrid.Rows.Count;
			int num = DaqFifo_dGrid.DisplayedRowCount(includePartialRow: false);
			int num2 = count - num;
			if (num2 < 0)
			{
				num2 = 0;
			}
			DaqFifo_dGrid.FirstDisplayedScrollingRowIndex = num2;
		}
	}

	private void PLOT_EnhancePlotStyle()
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		((FormsPlotBase)Variable_Plot).Plot.FigureBackground.Color = Colors.White;
		((FormsPlotBase)Variable_Plot).Plot.DataBackground.Color = Colors.White;
		DefaultGrid grid = ((FormsPlotBase)Variable_Plot).Plot.Grid;
		Color navy = Colors.Navy;
		grid.MajorLineColor = ((Color)(ref navy)).WithOpacity(0.25);
		((FormsPlotBase)Variable_Plot).Plot.Grid.MajorLinePattern = LinePattern.Dashed;
		((FormsPlotBase)Variable_Plot).Plot.Grid.MajorLineWidth = 0.9f;
		((FormsPlotBase)Variable_Plot).Plot.Font.Set("Times New Roman");
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
		this.components = new System.ComponentModel.Container();
		this.DaqFifo_dGrid = new System.Windows.Forms.DataGridView();
		this.RAW = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.SerFifoStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.QuadFIFO = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.MAT_ADDRESS = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.PIX_ADDRESS = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.TPM = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.CALIB = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Timestamp = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.CAL_DCO = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.CAL_DE = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.CAL_TIME = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.COUNT_1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.COUNT_0 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.COUNT_TOT = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.DCO0_CODE = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.DCO1_CODE = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.DaqFifoForm_menuStrip = new System.Windows.Forms.MenuStrip();
		this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.saveToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripSeparator();
		this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.ReadSingleFifo_but = new System.Windows.Forms.Button();
		this.FIFO_saveFileDialog = new System.Windows.Forms.SaveFileDialog();
		this.FifoReadRange_but = new System.Windows.Forms.Button();
		this.FifoNWord_UpDown = new System.Windows.Forms.NumericUpDown();
		this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
		this.PLOT_MatSelectALL_chkBox = new System.Windows.Forms.CheckBox();
		this.PLOT_MatSelect_UpDown = new System.Windows.Forms.NumericUpDown();
		this.PLOT_PixSelectALL_chkBox = new System.Windows.Forms.CheckBox();
		this.PLOT_PixSelect_UpDown = new System.Windows.Forms.NumericUpDown();
		this.PLOT_BinCount_UpDown = new System.Windows.Forms.NumericUpDown();
		this.PLOT_HistNormalize_chkBox = new System.Windows.Forms.CheckBox();
		this.FIFO_DropFakeData_chkBox = new System.Windows.Forms.CheckBox();
		this.PLOT_PixSelect2_UpDown = new System.Windows.Forms.NumericUpDown();
		this.PLOT_TAcode_UpDown = new System.Windows.Forms.NumericUpDown();
		this.ReadDataToGRID_but = new System.Windows.Forms.Button();
		this.ReadingSTOP_but = new System.Windows.Forms.Button();
		this.ReadDataToFILE_but = new System.Windows.Forms.Button();
		this.LoadDataToGRID_but = new System.Windows.Forms.Button();
		this.LoadDataToMEM_but = new System.Windows.Forms.Button();
		this.Plot_Panel = new System.Windows.Forms.Panel();
		this.PLOT_PlotGenerate_but = new System.Windows.Forms.Button();
		this.PLOT_ToggleDataType_but = new System.Windows.Forms.Button();
		this.PLOT_ToggleCAL_label = new System.Windows.Forms.Label();
		this.PLOT_ToggleMES_label = new System.Windows.Forms.Label();
		this.PLOT_PlotType_comboBox = new System.Windows.Forms.ComboBox();
		this.PLOT_PlotType_label = new System.Windows.Forms.Label();
		this.PLOT_XAxis_comboBox = new System.Windows.Forms.ComboBox();
		this.PLOT_XAxis_label = new System.Windows.Forms.Label();
		this.PLOT_YAxis_label = new System.Windows.Forms.Label();
		this.PLOT_YAxis_comboBox = new System.Windows.Forms.ComboBox();
		this.PLOT_MatSelect_label = new System.Windows.Forms.Label();
		this.PLOT_PixSelect_label = new System.Windows.Forms.Label();
		this.PLOT_PlotClear_but = new System.Windows.Forms.Button();
		this.PLOT_BinCount_label = new System.Windows.Forms.Label();
		this.SaveGRIDtoFILE_but = new System.Windows.Forms.Button();
		this.button1 = new System.Windows.Forms.Button();
		this.ClearGRIDonly_but = new System.Windows.Forms.Button();
		this.FifoReadEmpty_but = new System.Windows.Forms.Button();
		this.PlotAvg1_textBox = new System.Windows.Forms.TextBox();
		this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
		this.PlotSigma1_textBox = new System.Windows.Forms.TextBox();
		this.PlotAvg_label = new System.Windows.Forms.Label();
		this.PlotSigma_label = new System.Windows.Forms.Label();
		this.PLOT_PixSelect2_label = new System.Windows.Forms.Label();
		this.PLOT_TAcode_label = new System.Windows.Forms.Label();
		this.SaveALLtoFILE_but = new System.Windows.Forms.Button();
		this.PLOT_RemoveOutliers_chkBox = new System.Windows.Forms.CheckBox();
		this.PLOT_RemoveOutliers_numUpDown = new System.Windows.Forms.NumericUpDown();
		this.PLOT_RemoveOutliers_label = new System.Windows.Forms.Label();
		((System.ComponentModel.ISupportInitialize)this.DaqFifo_dGrid).BeginInit();
		this.DaqFifoForm_menuStrip.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.FifoNWord_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.PLOT_MatSelect_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.PLOT_PixSelect_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.PLOT_BinCount_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.PLOT_PixSelect2_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.PLOT_TAcode_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.PLOT_RemoveOutliers_numUpDown).BeginInit();
		base.SuspendLayout();
		this.DaqFifo_dGrid.AllowUserToAddRows = false;
		this.DaqFifo_dGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
		this.DaqFifo_dGrid.Columns.AddRange(this.RAW, this.SerFifoStatus, this.QuadFIFO, this.MAT_ADDRESS, this.PIX_ADDRESS, this.TPM, this.CALIB, this.Timestamp, this.CAL_DCO, this.CAL_DE, this.CAL_TIME, this.COUNT_1, this.COUNT_0, this.COUNT_TOT, this.DCO0_CODE, this.DCO1_CODE);
		this.DaqFifo_dGrid.Location = new System.Drawing.Point(0, 30);
		this.DaqFifo_dGrid.Name = "DaqFifo_dGrid";
		this.DaqFifo_dGrid.RowHeadersWidth = 50;
		this.DaqFifo_dGrid.Size = new System.Drawing.Size(805, 502);
		this.DaqFifo_dGrid.TabIndex = 0;
		this.RAW.HeaderText = "RAW_DATA";
		this.RAW.Name = "RAW";
		this.RAW.Width = 125;
		this.SerFifoStatus.HeaderText = "Fifo Status";
		this.SerFifoStatus.Name = "SerFifoStatus";
		this.SerFifoStatus.Width = 45;
		this.QuadFIFO.HeaderText = "NUM FIFO";
		this.QuadFIFO.Name = "QuadFIFO";
		this.QuadFIFO.Width = 45;
		this.MAT_ADDRESS.HeaderText = "MAT";
		this.MAT_ADDRESS.Name = "MAT_ADDRESS";
		this.MAT_ADDRESS.Width = 40;
		this.PIX_ADDRESS.HeaderText = "PIX";
		this.PIX_ADDRESS.Name = "PIX_ADDRESS";
		this.PIX_ADDRESS.Width = 40;
		this.TPM.HeaderText = "TPM";
		this.TPM.Name = "TPM";
		this.TPM.Width = 40;
		this.CALIB.HeaderText = "Calib";
		this.CALIB.Name = "CALIB";
		this.CALIB.Width = 40;
		this.Timestamp.HeaderText = "Time stamp";
		this.Timestamp.Name = "Timestamp";
		this.Timestamp.Width = 45;
		this.CAL_DCO.HeaderText = "DCO";
		this.CAL_DCO.Name = "CAL_DCO";
		this.CAL_DCO.Width = 40;
		this.CAL_DE.HeaderText = "DE";
		this.CAL_DE.Name = "CAL_DE";
		this.CAL_DE.Width = 40;
		this.CAL_TIME.HeaderText = "CAL Time";
		this.CAL_TIME.Name = "CAL_TIME";
		this.CAL_TIME.Width = 40;
		this.COUNT_1.HeaderText = "Cnt_1";
		this.COUNT_1.Name = "COUNT_1";
		this.COUNT_1.Width = 55;
		this.COUNT_0.HeaderText = "Cnt_0";
		this.COUNT_0.Name = "COUNT_0";
		this.COUNT_0.Width = 55;
		this.COUNT_TOT.HeaderText = "Cnt TOT";
		this.COUNT_TOT.Name = "COUNT_TOT";
		this.COUNT_TOT.Width = 55;
		this.DCO0_CODE.HeaderText = "DCO0 code";
		this.DCO0_CODE.Name = "DCO0_CODE";
		this.DCO0_CODE.Width = 40;
		this.DCO1_CODE.HeaderText = "DCO1 code";
		this.DCO1_CODE.Name = "DCO1_CODE";
		this.DCO1_CODE.Width = 40;
		this.DaqFifoForm_menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[1] { this.fileToolStripMenuItem });
		this.DaqFifoForm_menuStrip.Location = new System.Drawing.Point(0, 0);
		this.DaqFifoForm_menuStrip.Name = "DaqFifoForm_menuStrip";
		this.DaqFifoForm_menuStrip.Size = new System.Drawing.Size(984, 24);
		this.DaqFifoForm_menuStrip.TabIndex = 1;
		this.DaqFifoForm_menuStrip.Text = "menuStrip1";
		this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[3] { this.saveToolStripMenuItem, this.toolStripMenuItem1, this.exitToolStripMenuItem });
		this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
		this.fileToolStripMenuItem.Size = new System.Drawing.Size(37, 20);
		this.fileToolStripMenuItem.Text = "File";
		this.saveToolStripMenuItem.Name = "saveToolStripMenuItem";
		this.saveToolStripMenuItem.Size = new System.Drawing.Size(98, 22);
		this.saveToolStripMenuItem.Text = "Save";
		this.saveToolStripMenuItem.Click += new System.EventHandler(saveToolStripMenuItem_Click);
		this.toolStripMenuItem1.Name = "toolStripMenuItem1";
		this.toolStripMenuItem1.Size = new System.Drawing.Size(95, 6);
		this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
		this.exitToolStripMenuItem.Size = new System.Drawing.Size(98, 22);
		this.exitToolStripMenuItem.Text = "Exit";
		this.ReadSingleFifo_but.Location = new System.Drawing.Point(19, 559);
		this.ReadSingleFifo_but.Name = "ReadSingleFifo_but";
		this.ReadSingleFifo_but.Size = new System.Drawing.Size(101, 23);
		this.ReadSingleFifo_but.TabIndex = 2;
		this.ReadSingleFifo_but.Text = "Read Single";
		this.ReadSingleFifo_but.UseVisualStyleBackColor = true;
		this.ReadSingleFifo_but.Click += new System.EventHandler(ReadSingleFifo_but_Click);
		this.FifoReadRange_but.Location = new System.Drawing.Point(60, 582);
		this.FifoReadRange_but.Name = "FifoReadRange_but";
		this.FifoReadRange_but.Size = new System.Drawing.Size(60, 35);
		this.FifoReadRange_but.TabIndex = 3;
		this.FifoReadRange_but.Text = "Read N Words";
		this.FifoReadRange_but.UseVisualStyleBackColor = true;
		this.FifoReadRange_but.Click += new System.EventHandler(ReadNWordsFifo_but_Click);
		this.FifoNWord_UpDown.Location = new System.Drawing.Point(19, 591);
		this.FifoNWord_UpDown.Maximum = new decimal(new int[4] { 23, 0, 0, 0 });
		this.FifoNWord_UpDown.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.FifoNWord_UpDown.Name = "FifoNWord_UpDown";
		this.FifoNWord_UpDown.Size = new System.Drawing.Size(35, 20);
		this.FifoNWord_UpDown.TabIndex = 4;
		this.toolTip1.SetToolTip(this.FifoNWord_UpDown, "Number of words to read with the button on the right");
		this.FifoNWord_UpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.PLOT_MatSelectALL_chkBox.AutoSize = true;
		this.PLOT_MatSelectALL_chkBox.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.PLOT_MatSelectALL_chkBox.Location = new System.Drawing.Point(922, 289);
		this.PLOT_MatSelectALL_chkBox.Name = "PLOT_MatSelectALL_chkBox";
		this.PLOT_MatSelectALL_chkBox.Size = new System.Drawing.Size(43, 17);
		this.PLOT_MatSelectALL_chkBox.TabIndex = 38;
		this.PLOT_MatSelectALL_chkBox.Text = "All?";
		this.toolTip1.SetToolTip(this.PLOT_MatSelectALL_chkBox, "If checked, edits to Adj and Ctrl apply to ALL Pixels' DCOs");
		this.PLOT_MatSelectALL_chkBox.UseVisualStyleBackColor = true;
		this.PLOT_MatSelect_UpDown.Location = new System.Drawing.Point(877, 288);
		this.PLOT_MatSelect_UpDown.Maximum = new decimal(new int[4] { 15, 0, 0, 0 });
		this.PLOT_MatSelect_UpDown.Name = "PLOT_MatSelect_UpDown";
		this.PLOT_MatSelect_UpDown.Size = new System.Drawing.Size(39, 20);
		this.PLOT_MatSelect_UpDown.TabIndex = 36;
		this.toolTip1.SetToolTip(this.PLOT_MatSelect_UpDown, "Mattonella  Selector");
		this.PLOT_PixSelectALL_chkBox.AutoSize = true;
		this.PLOT_PixSelectALL_chkBox.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.PLOT_PixSelectALL_chkBox.Location = new System.Drawing.Point(922, 315);
		this.PLOT_PixSelectALL_chkBox.Name = "PLOT_PixSelectALL_chkBox";
		this.PLOT_PixSelectALL_chkBox.Size = new System.Drawing.Size(43, 17);
		this.PLOT_PixSelectALL_chkBox.TabIndex = 41;
		this.PLOT_PixSelectALL_chkBox.Text = "All?";
		this.toolTip1.SetToolTip(this.PLOT_PixSelectALL_chkBox, "If checked, edits to Adj and Ctrl apply to ALL Pixels' DCOs");
		this.PLOT_PixSelectALL_chkBox.UseVisualStyleBackColor = true;
		this.PLOT_PixSelect_UpDown.Location = new System.Drawing.Point(877, 314);
		this.PLOT_PixSelect_UpDown.Maximum = new decimal(new int[4] { 63, 0, 0, 0 });
		this.PLOT_PixSelect_UpDown.Name = "PLOT_PixSelect_UpDown";
		this.PLOT_PixSelect_UpDown.Size = new System.Drawing.Size(39, 20);
		this.PLOT_PixSelect_UpDown.TabIndex = 39;
		this.toolTip1.SetToolTip(this.PLOT_PixSelect_UpDown, "Pixel  Selector");
		this.PLOT_BinCount_UpDown.Location = new System.Drawing.Point(863, 163);
		this.PLOT_BinCount_UpDown.Maximum = new decimal(new int[4] { 5000, 0, 0, 0 });
		this.PLOT_BinCount_UpDown.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.PLOT_BinCount_UpDown.Name = "PLOT_BinCount_UpDown";
		this.PLOT_BinCount_UpDown.Size = new System.Drawing.Size(50, 20);
		this.PLOT_BinCount_UpDown.TabIndex = 43;
		this.toolTip1.SetToolTip(this.PLOT_BinCount_UpDown, "Pixel  Selector");
		this.PLOT_BinCount_UpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.PLOT_HistNormalize_chkBox.AutoSize = true;
		this.PLOT_HistNormalize_chkBox.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.PLOT_HistNormalize_chkBox.Location = new System.Drawing.Point(918, 164);
		this.PLOT_HistNormalize_chkBox.Name = "PLOT_HistNormalize_chkBox";
		this.PLOT_HistNormalize_chkBox.Size = new System.Drawing.Size(54, 17);
		this.PLOT_HistNormalize_chkBox.TabIndex = 45;
		this.PLOT_HistNormalize_chkBox.Text = "Norm.";
		this.toolTip1.SetToolTip(this.PLOT_HistNormalize_chkBox, "If checked, edits to Adj and Ctrl apply to ALL Pixels' DCOs");
		this.PLOT_HistNormalize_chkBox.UseVisualStyleBackColor = true;
		this.FIFO_DropFakeData_chkBox.AutoSize = true;
		this.FIFO_DropFakeData_chkBox.Checked = true;
		this.FIFO_DropFakeData_chkBox.CheckState = System.Windows.Forms.CheckState.Checked;
		this.FIFO_DropFakeData_chkBox.Location = new System.Drawing.Point(19, 642);
		this.FIFO_DropFakeData_chkBox.Name = "FIFO_DropFakeData_chkBox";
		this.FIFO_DropFakeData_chkBox.Size = new System.Drawing.Size(108, 17);
		this.FIFO_DropFakeData_chkBox.TabIndex = 49;
		this.FIFO_DropFakeData_chkBox.Text = "Drop Fake Data?";
		this.toolTip1.SetToolTip(this.FIFO_DropFakeData_chkBox, "Keep Checked during normal operation.\r\nIf checked, each time a FIFO entry is read while the FIFO is empty, it's deleted.\r\nUncheck only if you need to debug something related to the FIFO empty status.");
		this.FIFO_DropFakeData_chkBox.UseVisualStyleBackColor = true;
		this.PLOT_PixSelect2_UpDown.Location = new System.Drawing.Point(877, 340);
		this.PLOT_PixSelect2_UpDown.Maximum = new decimal(new int[4] { 63, 0, 0, 0 });
		this.PLOT_PixSelect2_UpDown.Name = "PLOT_PixSelect2_UpDown";
		this.PLOT_PixSelect2_UpDown.Size = new System.Drawing.Size(39, 20);
		this.PLOT_PixSelect2_UpDown.TabIndex = 55;
		this.toolTip1.SetToolTip(this.PLOT_PixSelect2_UpDown, "Pixel  Selector");
		this.PLOT_TAcode_UpDown.Location = new System.Drawing.Point(945, 340);
		this.PLOT_TAcode_UpDown.Maximum = new decimal(new int[4] { 15, 0, 0, 0 });
		this.PLOT_TAcode_UpDown.Name = "PLOT_TAcode_UpDown";
		this.PLOT_TAcode_UpDown.Size = new System.Drawing.Size(35, 20);
		this.PLOT_TAcode_UpDown.TabIndex = 57;
		this.toolTip1.SetToolTip(this.PLOT_TAcode_UpDown, "TA Phase Selector\r\n0 = All Phases");
		this.ReadDataToGRID_but.Location = new System.Drawing.Point(149, 559);
		this.ReadDataToGRID_but.Name = "ReadDataToGRID_but";
		this.ReadDataToGRID_but.Size = new System.Drawing.Size(128, 23);
		this.ReadDataToGRID_but.TabIndex = 5;
		this.ReadDataToGRID_but.Text = "Start Reading To GRID";
		this.ReadDataToGRID_but.UseVisualStyleBackColor = true;
		this.ReadDataToGRID_but.Click += new System.EventHandler(StartReadingButton_Click);
		this.ReadingSTOP_but.Location = new System.Drawing.Point(149, 609);
		this.ReadingSTOP_but.Name = "ReadingSTOP_but";
		this.ReadingSTOP_but.Size = new System.Drawing.Size(128, 23);
		this.ReadingSTOP_but.TabIndex = 6;
		this.ReadingSTOP_but.Text = "STOP Reading";
		this.ReadingSTOP_but.UseVisualStyleBackColor = true;
		this.ReadingSTOP_but.Click += new System.EventHandler(StopReadingButton_Click);
		this.ReadDataToFILE_but.Location = new System.Drawing.Point(149, 584);
		this.ReadDataToFILE_but.Name = "ReadDataToFILE_but";
		this.ReadDataToFILE_but.Size = new System.Drawing.Size(128, 23);
		this.ReadDataToFILE_but.TabIndex = 7;
		this.ReadDataToFILE_but.Text = "Start Reading To FILE";
		this.ReadDataToFILE_but.UseVisualStyleBackColor = true;
		this.ReadDataToFILE_but.Click += new System.EventHandler(StartReadingToFileButton_Click);
		this.LoadDataToGRID_but.Location = new System.Drawing.Point(413, 559);
		this.LoadDataToGRID_but.Name = "LoadDataToGRID_but";
		this.LoadDataToGRID_but.Size = new System.Drawing.Size(125, 23);
		this.LoadDataToGRID_but.TabIndex = 8;
		this.LoadDataToGRID_but.Text = "Load Data to GRID";
		this.LoadDataToGRID_but.UseVisualStyleBackColor = true;
		this.LoadDataToGRID_but.Click += new System.EventHandler(btnLoadDataToGrid_Click);
		this.LoadDataToMEM_but.Location = new System.Drawing.Point(413, 584);
		this.LoadDataToMEM_but.Name = "LoadDataToMEM_but";
		this.LoadDataToMEM_but.Size = new System.Drawing.Size(125, 23);
		this.LoadDataToMEM_but.TabIndex = 9;
		this.LoadDataToMEM_but.Text = "Load Data to Memory";
		this.LoadDataToMEM_but.UseVisualStyleBackColor = true;
		this.LoadDataToMEM_but.Click += new System.EventHandler(btnLoadDataHidden_Click);
		this.Plot_Panel.BackColor = System.Drawing.Color.White;
		this.Plot_Panel.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
		this.Plot_Panel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
		this.Plot_Panel.Location = new System.Drawing.Point(0, 58);
		this.Plot_Panel.Name = "Plot_Panel";
		this.Plot_Panel.Size = new System.Drawing.Size(805, 492);
		this.Plot_Panel.TabIndex = 10;
		this.PLOT_PlotGenerate_but.Location = new System.Drawing.Point(811, 558);
		this.PLOT_PlotGenerate_but.Name = "PLOT_PlotGenerate_but";
		this.PLOT_PlotGenerate_but.Size = new System.Drawing.Size(164, 30);
		this.PLOT_PlotGenerate_but.TabIndex = 11;
		this.PLOT_PlotGenerate_but.Text = "Generate Plot";
		this.PLOT_PlotGenerate_but.UseVisualStyleBackColor = true;
		this.PLOT_PlotGenerate_but.Click += new System.EventHandler(PlotGenerateButton_Click);
		this.PLOT_ToggleDataType_but.BackColor = System.Drawing.Color.Tomato;
		this.PLOT_ToggleDataType_but.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.PLOT_ToggleDataType_but.Font = new System.Drawing.Font("Microsoft Sans Serif", 9f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		this.PLOT_ToggleDataType_but.ForeColor = System.Drawing.Color.Black;
		this.PLOT_ToggleDataType_but.Location = new System.Drawing.Point(811, 58);
		this.PLOT_ToggleDataType_but.Name = "PLOT_ToggleDataType_but";
		this.PLOT_ToggleDataType_but.Size = new System.Drawing.Size(164, 31);
		this.PLOT_ToggleDataType_but.TabIndex = 12;
		this.PLOT_ToggleDataType_but.Text = "SWITCH DATA TYPE";
		this.PLOT_ToggleDataType_but.UseVisualStyleBackColor = false;
		this.PLOT_ToggleDataType_but.Click += new System.EventHandler(PlotToggleTypeButton_Click);
		this.PLOT_ToggleCAL_label.AutoSize = true;
		this.PLOT_ToggleCAL_label.BackColor = System.Drawing.Color.Transparent;
		this.PLOT_ToggleCAL_label.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		this.PLOT_ToggleCAL_label.Location = new System.Drawing.Point(812, 91);
		this.PLOT_ToggleCAL_label.Name = "PLOT_ToggleCAL_label";
		this.PLOT_ToggleCAL_label.Size = new System.Drawing.Size(89, 13);
		this.PLOT_ToggleCAL_label.TabIndex = 13;
		this.PLOT_ToggleCAL_label.Text = "CALIBRATION";
		this.PLOT_ToggleMES_label.AutoSize = true;
		this.PLOT_ToggleMES_label.BackColor = System.Drawing.Color.Transparent;
		this.PLOT_ToggleMES_label.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		this.PLOT_ToggleMES_label.Location = new System.Drawing.Point(907, 91);
		this.PLOT_ToggleMES_label.Name = "PLOT_ToggleMES_label";
		this.PLOT_ToggleMES_label.Size = new System.Drawing.Size(67, 13);
		this.PLOT_ToggleMES_label.TabIndex = 14;
		this.PLOT_ToggleMES_label.Text = "MEASURE";
		this.PLOT_PlotType_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.PLOT_PlotType_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.PLOT_PlotType_comboBox.FormattingEnabled = true;
		this.PLOT_PlotType_comboBox.Items.AddRange(new object[3] { "Scatter X vs Y", "Histogram", "Heatmap Maybe?" });
		this.PLOT_PlotType_comboBox.Location = new System.Drawing.Point(811, 136);
		this.PLOT_PlotType_comboBox.Name = "PLOT_PlotType_comboBox";
		this.PLOT_PlotType_comboBox.Size = new System.Drawing.Size(163, 21);
		this.PLOT_PlotType_comboBox.TabIndex = 15;
		this.PLOT_PlotType_comboBox.SelectedIndexChanged += new System.EventHandler(PlotType_ComboBox_IndexChanged);
		this.PLOT_PlotType_label.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom;
		this.PLOT_PlotType_label.AutoSize = true;
		this.PLOT_PlotType_label.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		this.PLOT_PlotType_label.Location = new System.Drawing.Point(829, 117);
		this.PLOT_PlotType_label.Name = "PLOT_PlotType_label";
		this.PLOT_PlotType_label.Size = new System.Drawing.Size(123, 16);
		this.PLOT_PlotType_label.TabIndex = 16;
		this.PLOT_PlotType_label.Text = "Select Plot Type";
		this.PLOT_PlotType_label.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
		this.PLOT_XAxis_comboBox.AutoCompleteCustomSource.AddRange(new string[3] { "Scatter X vs Y", "Histogram", "Heatmap Maybe?" });
		this.PLOT_XAxis_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.PLOT_XAxis_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.PLOT_XAxis_comboBox.FormattingEnabled = true;
		this.PLOT_XAxis_comboBox.Location = new System.Drawing.Point(857, 189);
		this.PLOT_XAxis_comboBox.Name = "PLOT_XAxis_comboBox";
		this.PLOT_XAxis_comboBox.Size = new System.Drawing.Size(117, 21);
		this.PLOT_XAxis_comboBox.TabIndex = 17;
		this.PLOT_XAxis_comboBox.SelectedIndexChanged += new System.EventHandler(XAxis_ComboBox_IndexChanged);
		this.PLOT_XAxis_label.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom;
		this.PLOT_XAxis_label.AutoSize = true;
		this.PLOT_XAxis_label.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		this.PLOT_XAxis_label.Location = new System.Drawing.Point(812, 192);
		this.PLOT_XAxis_label.Name = "PLOT_XAxis_label";
		this.PLOT_XAxis_label.Size = new System.Drawing.Size(42, 13);
		this.PLOT_XAxis_label.TabIndex = 18;
		this.PLOT_XAxis_label.Text = "X Axis";
		this.PLOT_XAxis_label.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
		this.PLOT_YAxis_label.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom;
		this.PLOT_YAxis_label.AutoSize = true;
		this.PLOT_YAxis_label.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		this.PLOT_YAxis_label.Location = new System.Drawing.Point(813, 219);
		this.PLOT_YAxis_label.Name = "PLOT_YAxis_label";
		this.PLOT_YAxis_label.Size = new System.Drawing.Size(42, 13);
		this.PLOT_YAxis_label.TabIndex = 20;
		this.PLOT_YAxis_label.Text = "Y Axis";
		this.PLOT_YAxis_label.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
		this.PLOT_YAxis_comboBox.AutoCompleteCustomSource.AddRange(new string[3] { "Scatter X vs Y", "Histogram", "Heatmap Maybe?" });
		this.PLOT_YAxis_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.PLOT_YAxis_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.PLOT_YAxis_comboBox.FormattingEnabled = true;
		this.PLOT_YAxis_comboBox.Location = new System.Drawing.Point(858, 216);
		this.PLOT_YAxis_comboBox.Name = "PLOT_YAxis_comboBox";
		this.PLOT_YAxis_comboBox.Size = new System.Drawing.Size(117, 21);
		this.PLOT_YAxis_comboBox.TabIndex = 19;
		this.PLOT_MatSelect_label.AutoSize = true;
		this.PLOT_MatSelect_label.Location = new System.Drawing.Point(815, 290);
		this.PLOT_MatSelect_label.Name = "PLOT_MatSelect_label";
		this.PLOT_MatSelect_label.Size = new System.Drawing.Size(56, 13);
		this.PLOT_MatSelect_label.TabIndex = 37;
		this.PLOT_MatSelect_label.Text = "Mattonella";
		this.PLOT_PixSelect_label.AutoSize = true;
		this.PLOT_PixSelect_label.Location = new System.Drawing.Point(815, 316);
		this.PLOT_PixSelect_label.Name = "PLOT_PixSelect_label";
		this.PLOT_PixSelect_label.Size = new System.Drawing.Size(29, 13);
		this.PLOT_PixSelect_label.TabIndex = 40;
		this.PLOT_PixSelect_label.Text = "Pixel";
		this.PLOT_PlotClear_but.Location = new System.Drawing.Point(811, 595);
		this.PLOT_PlotClear_but.Name = "PLOT_PlotClear_but";
		this.PLOT_PlotClear_but.Size = new System.Drawing.Size(164, 30);
		this.PLOT_PlotClear_but.TabIndex = 42;
		this.PLOT_PlotClear_but.Text = "Clear and Hide Plot";
		this.PLOT_PlotClear_but.UseVisualStyleBackColor = true;
		this.PLOT_PlotClear_but.Click += new System.EventHandler(PlotClearButton_Click);
		this.PLOT_BinCount_label.AutoSize = true;
		this.PLOT_BinCount_label.Location = new System.Drawing.Point(811, 165);
		this.PLOT_BinCount_label.Name = "PLOT_BinCount_label";
		this.PLOT_BinCount_label.Size = new System.Drawing.Size(53, 13);
		this.PLOT_BinCount_label.TabIndex = 44;
		this.PLOT_BinCount_label.Text = "Bin Count";
		this.SaveGRIDtoFILE_but.Location = new System.Drawing.Point(282, 559);
		this.SaveGRIDtoFILE_but.Name = "SaveGRIDtoFILE_but";
		this.SaveGRIDtoFILE_but.Size = new System.Drawing.Size(125, 23);
		this.SaveGRIDtoFILE_but.TabIndex = 46;
		this.SaveGRIDtoFILE_but.Text = "Save GRID To FILE";
		this.SaveGRIDtoFILE_but.UseVisualStyleBackColor = true;
		this.SaveGRIDtoFILE_but.Click += new System.EventHandler(saveToolStripMenuItem_Click);
		this.button1.BackColor = System.Drawing.Color.Tomato;
		this.button1.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.button1.Location = new System.Drawing.Point(282, 609);
		this.button1.Name = "button1";
		this.button1.Size = new System.Drawing.Size(125, 23);
		this.button1.TabIndex = 47;
		this.button1.Text = "Clear Data and GRID";
		this.button1.UseVisualStyleBackColor = false;
		this.button1.Click += new System.EventHandler(btnClearData_Click);
		this.ClearGRIDonly_but.Location = new System.Drawing.Point(282, 584);
		this.ClearGRIDonly_but.Name = "ClearGRIDonly_but";
		this.ClearGRIDonly_but.Size = new System.Drawing.Size(125, 23);
		this.ClearGRIDonly_but.TabIndex = 48;
		this.ClearGRIDonly_but.Text = "Clear GRID only";
		this.ClearGRIDonly_but.UseVisualStyleBackColor = true;
		this.ClearGRIDonly_but.Click += new System.EventHandler(btnClearGrid_Click);
		this.FifoReadEmpty_but.Location = new System.Drawing.Point(19, 617);
		this.FifoReadEmpty_but.Name = "FifoReadEmpty_but";
		this.FifoReadEmpty_but.Size = new System.Drawing.Size(101, 23);
		this.FifoReadEmpty_but.TabIndex = 50;
		this.FifoReadEmpty_but.Text = "Read Until Empty";
		this.FifoReadEmpty_but.UseVisualStyleBackColor = true;
		this.FifoReadEmpty_but.Click += new System.EventHandler(FifoReadUntilEmpty_but_Click);
		this.PlotAvg1_textBox.BackColor = System.Drawing.Color.White;
		this.PlotAvg1_textBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
		this.PlotAvg1_textBox.Location = new System.Drawing.Point(832, 261);
		this.PlotAvg1_textBox.Name = "PlotAvg1_textBox";
		this.PlotAvg1_textBox.ReadOnly = true;
		this.PlotAvg1_textBox.Size = new System.Drawing.Size(62, 20);
		this.PlotAvg1_textBox.TabIndex = 51;
		this.contextMenuStrip1.Name = "contextMenuStrip1";
		this.contextMenuStrip1.Size = new System.Drawing.Size(61, 4);
		this.PlotSigma1_textBox.BackColor = System.Drawing.Color.White;
		this.PlotSigma1_textBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
		this.PlotSigma1_textBox.Location = new System.Drawing.Point(903, 261);
		this.PlotSigma1_textBox.Name = "PlotSigma1_textBox";
		this.PlotSigma1_textBox.ReadOnly = true;
		this.PlotSigma1_textBox.Size = new System.Drawing.Size(62, 20);
		this.PlotSigma1_textBox.TabIndex = 52;
		this.PlotAvg_label.AutoSize = true;
		this.PlotAvg_label.Location = new System.Drawing.Point(836, 245);
		this.PlotAvg_label.Name = "PlotAvg_label";
		this.PlotAvg_label.Size = new System.Drawing.Size(34, 13);
		this.PlotAvg_label.TabIndex = 53;
		this.PlotAvg_label.Text = "Mean";
		this.PlotSigma_label.AutoSize = true;
		this.PlotSigma_label.Location = new System.Drawing.Point(907, 245);
		this.PlotSigma_label.Name = "PlotSigma_label";
		this.PlotSigma_label.Size = new System.Drawing.Size(36, 13);
		this.PlotSigma_label.TabIndex = 54;
		this.PlotSigma_label.Text = "Sigma";
		this.PLOT_PixSelect2_label.AutoSize = true;
		this.PLOT_PixSelect2_label.Location = new System.Drawing.Point(815, 342);
		this.PLOT_PixSelect2_label.Name = "PLOT_PixSelect2_label";
		this.PLOT_PixSelect2_label.Size = new System.Drawing.Size(38, 13);
		this.PLOT_PixSelect2_label.TabIndex = 56;
		this.PLOT_PixSelect2_label.Text = "Pixel 2";
		this.PLOT_TAcode_label.AutoSize = true;
		this.PLOT_TAcode_label.Location = new System.Drawing.Point(921, 342);
		this.PLOT_TAcode_label.Name = "PLOT_TAcode_label";
		this.PLOT_TAcode_label.Size = new System.Drawing.Size(21, 13);
		this.PLOT_TAcode_label.TabIndex = 58;
		this.PLOT_TAcode_label.Text = "TA";
		this.SaveALLtoFILE_but.Location = new System.Drawing.Point(282, 636);
		this.SaveALLtoFILE_but.Name = "SaveALLtoFILE_but";
		this.SaveALLtoFILE_but.Size = new System.Drawing.Size(125, 23);
		this.SaveALLtoFILE_but.TabIndex = 59;
		this.SaveALLtoFILE_but.Text = "Save All Data To FILE";
		this.SaveALLtoFILE_but.UseVisualStyleBackColor = true;
		this.SaveALLtoFILE_but.Click += new System.EventHandler(btnSaveWholeDataToFile_Click);
		this.PLOT_RemoveOutliers_chkBox.AutoSize = true;
		this.PLOT_RemoveOutliers_chkBox.Location = new System.Drawing.Point(815, 524);
		this.PLOT_RemoveOutliers_chkBox.Name = "PLOT_RemoveOutliers_chkBox";
		this.PLOT_RemoveOutliers_chkBox.Size = new System.Drawing.Size(66, 30);
		this.PLOT_RemoveOutliers_chkBox.TabIndex = 60;
		this.PLOT_RemoveOutliers_chkBox.Text = "Remove\r\nOutliers";
		this.PLOT_RemoveOutliers_chkBox.UseVisualStyleBackColor = true;
		this.PLOT_RemoveOutliers_numUpDown.DecimalPlaces = 2;
		this.PLOT_RemoveOutliers_numUpDown.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.PLOT_RemoveOutliers_numUpDown.Location = new System.Drawing.Point(921, 536);
		this.PLOT_RemoveOutliers_numUpDown.Minimum = new decimal(new int[4] { 2, 0, 0, 65536 });
		this.PLOT_RemoveOutliers_numUpDown.Name = "PLOT_RemoveOutliers_numUpDown";
		this.PLOT_RemoveOutliers_numUpDown.Size = new System.Drawing.Size(53, 20);
		this.PLOT_RemoveOutliers_numUpDown.TabIndex = 61;
		this.PLOT_RemoveOutliers_numUpDown.Value = new decimal(new int[4] { 300, 0, 0, 131072 });
		this.PLOT_RemoveOutliers_label.AutoSize = true;
		this.PLOT_RemoveOutliers_label.Location = new System.Drawing.Point(887, 522);
		this.PLOT_RemoveOutliers_label.Name = "PLOT_RemoveOutliers_label";
		this.PLOT_RemoveOutliers_label.Size = new System.Drawing.Size(90, 13);
		this.PLOT_RemoveOutliers_label.TabIndex = 62;
		this.PLOT_RemoveOutliers_label.Text = "Threshold (sigma)";
		base.AutoScaleDimensions = new System.Drawing.SizeF(6f, 13f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		base.ClientSize = new System.Drawing.Size(984, 661);
		base.Controls.Add(this.PLOT_RemoveOutliers_label);
		base.Controls.Add(this.PLOT_RemoveOutliers_numUpDown);
		base.Controls.Add(this.PLOT_RemoveOutliers_chkBox);
		base.Controls.Add(this.SaveALLtoFILE_but);
		base.Controls.Add(this.PLOT_TAcode_label);
		base.Controls.Add(this.PLOT_TAcode_UpDown);
		base.Controls.Add(this.PLOT_PixSelect2_UpDown);
		base.Controls.Add(this.PLOT_PixSelect2_label);
		base.Controls.Add(this.PlotSigma_label);
		base.Controls.Add(this.PlotAvg_label);
		base.Controls.Add(this.PlotSigma1_textBox);
		base.Controls.Add(this.PlotAvg1_textBox);
		base.Controls.Add(this.FifoReadEmpty_but);
		base.Controls.Add(this.FIFO_DropFakeData_chkBox);
		base.Controls.Add(this.ClearGRIDonly_but);
		base.Controls.Add(this.button1);
		base.Controls.Add(this.SaveGRIDtoFILE_but);
		base.Controls.Add(this.PLOT_BinCount_UpDown);
		base.Controls.Add(this.PLOT_BinCount_label);
		base.Controls.Add(this.PLOT_PlotClear_but);
		base.Controls.Add(this.PLOT_PixSelectALL_chkBox);
		base.Controls.Add(this.PLOT_PixSelect_UpDown);
		base.Controls.Add(this.PLOT_PixSelect_label);
		base.Controls.Add(this.PLOT_MatSelectALL_chkBox);
		base.Controls.Add(this.PLOT_MatSelect_UpDown);
		base.Controls.Add(this.PLOT_MatSelect_label);
		base.Controls.Add(this.PLOT_YAxis_label);
		base.Controls.Add(this.PLOT_YAxis_comboBox);
		base.Controls.Add(this.PLOT_XAxis_label);
		base.Controls.Add(this.PLOT_XAxis_comboBox);
		base.Controls.Add(this.PLOT_PlotType_label);
		base.Controls.Add(this.PLOT_PlotType_comboBox);
		base.Controls.Add(this.PLOT_ToggleMES_label);
		base.Controls.Add(this.PLOT_ToggleCAL_label);
		base.Controls.Add(this.PLOT_ToggleDataType_but);
		base.Controls.Add(this.PLOT_PlotGenerate_but);
		base.Controls.Add(this.Plot_Panel);
		base.Controls.Add(this.LoadDataToMEM_but);
		base.Controls.Add(this.LoadDataToGRID_but);
		base.Controls.Add(this.ReadDataToFILE_but);
		base.Controls.Add(this.ReadingSTOP_but);
		base.Controls.Add(this.ReadDataToGRID_but);
		base.Controls.Add(this.FifoNWord_UpDown);
		base.Controls.Add(this.FifoReadRange_but);
		base.Controls.Add(this.ReadSingleFifo_but);
		base.Controls.Add(this.DaqFifo_dGrid);
		base.Controls.Add(this.DaqFifoForm_menuStrip);
		base.Controls.Add(this.PLOT_HistNormalize_chkBox);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
		base.MainMenuStrip = this.DaqFifoForm_menuStrip;
		base.Name = "DaqFifoForm";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "DAQ FIFO";
		((System.ComponentModel.ISupportInitialize)this.DaqFifo_dGrid).EndInit();
		this.DaqFifoForm_menuStrip.ResumeLayout(false);
		this.DaqFifoForm_menuStrip.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.FifoNWord_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.PLOT_MatSelect_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.PLOT_PixSelect_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.PLOT_BinCount_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.PLOT_PixSelect2_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.PLOT_TAcode_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.PLOT_RemoveOutliers_numUpDown).EndInit();
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
