#define DEBUG
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MathNet.Numerics.Distributions;
using MathNet.Numerics.Statistics;

namespace tb_Ignite64;

public class MainForm : Form
{
	public class DataEntry : IDisposable
	{
		public int Order { get; set; }

		public string RAW { get; set; }

		public ushort MAT { get; set; }

		public ushort PIX { get; set; }

		public ushort CAL_Mode { get; set; }

		public int Cnt_TOT { get; set; }

		public ushort? DCO { get; set; }

		public ushort? DE { get; set; }

		public ushort? CAL_Time { get; set; }

		public ushort? AdjCtrl_DCO0 { get; set; }

		public ushort? AdjCtrl_DCO1 { get; set; }

		public double? DCO0_T_picoS { get; set; }

		public double? DCO1_T_picoS { get; set; }

		public double? TA_picoS { get; set; }

		public double? TOT_picoS { get; set; }

		public ushort? TimeStamp { get; set; }

		public ushort? TA_Code { get; set; }

		public ushort? TOT_Code { get; set; }

		protected bool Disposed { get; private set; }

		public double DCO_Period(ushort? de, ushort? cal_time, int DCO_TOT)
		{
			if (CAL_Mode == 1)
			{
				if (de == 0)
				{
					return Math.Pow(2.0, (int)cal_time.Value) * 400.0 * 1000.0 / (double)DCO_TOT;
				}
				if (de == 1)
				{
					return Math.Pow(2.0, (int)cal_time.Value) * 400.0 * 1000.0 / (2.0 * (double)DCO_TOT);
				}
				MessageBox.Show("ERROR: Trying to calculate DCO Period,  DE value is not 0 OR 1");
				return 0.0;
			}
			MessageBox.Show("ERROR: Trying to calculate DCO Period when CAL_Mode is NOT 1");
			return 0.0;
		}

		public void SetVariables_CALIBRATION(ushort? dco, ushort? de, ushort? cal_time, int DCO_TOT)
		{
			if (CAL_Mode == 1)
			{
				DCO = dco;
				DE = de;
				CAL_Time = cal_time;
				if (dco.Value == 0)
				{
					DCO0_T_picoS = DCO_Period(de, cal_time, DCO_TOT);
				}
				else if (dco.Value == 1)
				{
					DCO1_T_picoS = DCO_Period(de, cal_time, DCO_TOT);
				}
				else
				{
					MessageBox.Show("ERROR: Trying to assign DCO Period,  DCO value is not 0 OR 1");
				}
			}
		}

		public void SetVariables_MEASURE(int counts_0, int counts_1)
		{
			TA_picoS = (double)(counts_0 - 1) * DCO0_T_picoS - (double)(counts_1 - 2) * DCO1_T_picoS;
			TOT_picoS = (double)(Cnt_TOT - 1) * DCO0_T_picoS;
			DataIndex.PIX_TA_Entry item = new DataIndex.PIX_TA_Entry
			{
				Order = Order,
				TA = TA_picoS.Value,
				TOT = TOT_picoS.Value
			};
			LoadedData.TA_MAP_100[Cur_Quad, MAT, PIX].Add(item);
		}

		~DataEntry()
		{
			Dispose(disposing: false);
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!Disposed)
			{
				Disposed = true;
			}
		}
	}

	public struct StructuredKey : IEquatable<StructuredKey>
	{
		public ushort MAT { get; }

		public ushort PIX { get; }

		public ushort CAL_Mode { get; }

		public ushort? DCO { get; }

		public StructuredKey(ushort mat, ushort pix, ushort calMode, ushort? dco)
		{
			MAT = mat;
			PIX = pix;
			CAL_Mode = calMode;
			DCO = dco;
		}

		public bool Find_Any_ushortMaxValue(StructuredKey Key_To_Check)
		{
			return (MAT == Key_To_Check.MAT || Key_To_Check.MAT == ushort.MaxValue) && (PIX == Key_To_Check.PIX || Key_To_Check.PIX == ushort.MaxValue) && (CAL_Mode == Key_To_Check.CAL_Mode || Key_To_Check.CAL_Mode == ushort.MaxValue) && (DCO == Key_To_Check.DCO || Key_To_Check.DCO == ushort.MaxValue);
		}

		public override bool Equals(object obj)
		{
			if (obj is StructuredKey other)
			{
				return Equals(other);
			}
			return false;
		}

		public bool Equals(StructuredKey other)
		{
			if (MAT == other.MAT && PIX == other.PIX && CAL_Mode == other.CAL_Mode)
			{
				return DCO == other.DCO;
			}
			return false;
		}

		public override int GetHashCode()
		{
			int num = 17;
			num = num * 31 + MAT.GetHashCode();
			num = num * 31 + PIX.GetHashCode();
			num = num * 31 + CAL_Mode.GetHashCode();
			return num * 31 + DCO.GetHashCode();
		}
	}

	public class DataIndex
	{
		public struct PIX_TA_Entry
		{
			public int Order;

			public double TA;

			public double TOT;
		}

		private Dictionary<StructuredKey, List<DataEntry>> Custom_Entries;

		private Dictionary<(ushort MAT, ushort PIX), List<DataEntry>> MAT_PIX;

		private Dictionary<(ushort CAL_Mode, ushort? DCO), List<DataEntry>> CAL_DCO;

		public int N_Stored_Entries;

		public int measure_counter;

		public int calibra_counter;

		public int useless_counter;

		public double[,,,] CAL_Matrix;

		public double[,,] Resolution_Matrix;

		public int[,,,] DCO_ConfPairs_Matrix;

		public FIFO_like_Array<PIX_TA_Entry>[,,] TA_MAP_100 = new FIFO_like_Array<PIX_TA_Entry>[4, 16, 64];

		public int n_read_from_last_save { get; set; }

		public DataIndex()
		{
			Custom_Entries = new Dictionary<StructuredKey, List<DataEntry>>();
			MAT_PIX = new Dictionary<(ushort, ushort), List<DataEntry>>();
			CAL_DCO = new Dictionary<(ushort, ushort?), List<DataEntry>>();
			CAL_Matrix = new double[4, 16, 64, 2];
			Resolution_Matrix = new double[4, 16, 64];
			DCO_ConfPairs_Matrix = new int[4, 16, 64, 2];
			N_Stored_Entries = 0;
			n_read_from_last_save = 0;
			for (int i = 0; i < 4; i++)
			{
				for (int j = 0; j < 16; j++)
				{
					for (int k = 0; k < 64; k++)
					{
						TA_MAP_100[i, j, k] = new FIFO_like_Array<PIX_TA_Entry>(1000);
					}
				}
			}
		}

		public void Add(DataEntry entry)
		{
			entry.Order = N_Stored_Entries;
			N_Stored_Entries++;
			StructuredKey key = new StructuredKey(entry.MAT, entry.PIX, entry.CAL_Mode, entry.DCO);
			if (!Custom_Entries.ContainsKey(key))
			{
				Custom_Entries[key] = new List<DataEntry>();
			}
			Custom_Entries[key].Add(entry);
			(ushort, ushort) key2 = (entry.MAT, entry.PIX);
			if (!MAT_PIX.ContainsKey(key2))
			{
				MAT_PIX[key2] = new List<DataEntry>();
			}
			MAT_PIX[key2].Add(entry);
			(ushort, ushort?) key3 = (entry.CAL_Mode, entry.DCO);
			if (!CAL_DCO.ContainsKey(key3))
			{
				CAL_DCO[key3] = new List<DataEntry>();
			}
			CAL_DCO[key3].Add(entry);
		}

		public List<DataEntry> GetByCustomKey(StructuredKey key)
		{
			Custom_Entries.TryGetValue(key, out var value);
			return value ?? new List<DataEntry>();
		}

		public List<DataEntry> GetByMAT_PIX(ushort mat, ushort pix)
		{
			if (mat == ushort.MaxValue)
			{
				return MAT_PIX.Where((KeyValuePair<(ushort MAT, ushort PIX), List<DataEntry>> kvp) => kvp.Key.PIX == pix).SelectMany((KeyValuePair<(ushort MAT, ushort PIX), List<DataEntry>> kvp) => kvp.Value).ToList();
			}
			if (pix == ushort.MaxValue)
			{
				return MAT_PIX.Where((KeyValuePair<(ushort MAT, ushort PIX), List<DataEntry>> kvp) => kvp.Key.MAT == mat).SelectMany((KeyValuePair<(ushort MAT, ushort PIX), List<DataEntry>> kvp) => kvp.Value).ToList();
			}
			(ushort, ushort) key = (mat, pix);
			MAT_PIX.TryGetValue(key, out var value);
			return value ?? new List<DataEntry>();
		}

		public List<DataEntry> GetByCAL_DCO(ushort calMode, ushort dco)
		{
			if (dco == ushort.MaxValue)
			{
				return CAL_DCO.Where((KeyValuePair<(ushort CAL_Mode, ushort? DCO), List<DataEntry>> kvp) => kvp.Key.CAL_Mode == calMode).SelectMany((KeyValuePair<(ushort CAL_Mode, ushort? DCO), List<DataEntry>> kvp) => kvp.Value).ToList();
			}
			if (calMode == ushort.MaxValue)
			{
				return CAL_DCO.Where((KeyValuePair<(ushort CAL_Mode, ushort? DCO), List<DataEntry>> kvp) => kvp.Key.DCO == dco).SelectMany((KeyValuePair<(ushort CAL_Mode, ushort? DCO), List<DataEntry>> kvp) => kvp.Value).ToList();
			}
			(ushort, ushort) tuple = (calMode, dco);
			Dictionary<(ushort CAL_Mode, ushort? DCO), List<DataEntry>> cAL_DCO = CAL_DCO;
			(ushort, ushort) tuple2 = tuple;
			cAL_DCO.TryGetValue((tuple2.Item1, tuple2.Item2), out var value);
			return value ?? new List<DataEntry>();
		}

		public List<DataEntry> GetAllEntriesInOrder()
		{
			return (from entry in Custom_Entries.SelectMany((KeyValuePair<StructuredKey, List<DataEntry>> kvp) => kvp.Value)
				orderby entry.Order
				select entry).ToList();
		}

		public List<DataEntry> GetByCustomKeyWildcard(StructuredKey key)
		{
			return Custom_Entries.Where((KeyValuePair<StructuredKey, List<DataEntry>> kvp) => (key.MAT == ushort.MaxValue || kvp.Key.MAT == key.MAT) && (key.PIX == ushort.MaxValue || kvp.Key.PIX == key.PIX) && (key.CAL_Mode == ushort.MaxValue || kvp.Key.CAL_Mode == key.CAL_Mode) && (key.DCO == ushort.MaxValue || kvp.Key.DCO == key.DCO)).SelectMany((KeyValuePair<StructuredKey, List<DataEntry>> kvp) => kvp.Value).ToList();
		}

		private string Calibration_ToString()
		{
			string text = "";
			text = text + "QUAD".PadRight(8) + "\t" + "MAT".PadRight(8) + "\t" + "PIX".PadRight(8) + "\t" + "DCO0".PadRight(8) + "\t" + "DCO1".PadRight(8) + "\n";
			for (int i = 0; i < 4; i++)
			{
				for (int j = 0; j < 16; j++)
				{
					for (int k = 0; k < 64; k++)
					{
						string text2 = i.ToString().PadRight(8) + "\t" + j.ToString().PadRight(8) + "\t" + k.ToString().PadRight(8) + "\t" + DCO_ConfPairs_Matrix[i, j, k, 0].ToString().PadRight(8) + "\t" + DCO_ConfPairs_Matrix[i, j, k, 1].ToString().PadRight(8) + "\n";
						text += text2;
					}
				}
			}
			return text;
		}

		public void SaveCalibrationConfigurationToFile()
		{
			SaveFileDialog saveFileDialog = new SaveFileDialog();
			saveFileDialog.FileName = "CAL_Configuration_" + DateTime.Now.ToString("yy.MM.dd.HH.mm") + ".txt";
			if (saveFileDialog.ShowDialog() == DialogResult.OK)
			{
				string fileName = saveFileDialog.FileName;
				using StreamWriter streamWriter = new StreamWriter(fileName);
				streamWriter.Write(Calibration_ToString());
				streamWriter.WriteLine();
			}
		}

		public void LoadDataFromFile()
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog();
			openFileDialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
			openFileDialog.Title = "Select a Text File";
			if (openFileDialog.ShowDialog() == DialogResult.OK)
			{
				string fileName = openFileDialog.FileName;
				ReadData(fileName);
			}
		}

		private void ReadData(string filePath)
		{
			try
			{
				IEnumerable<string> enumerable = File.ReadAllLines(filePath).Skip(1);
				foreach (string item in enumerable)
				{
					string[] array = item.Split(new char[2] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
					if (array.Length < 13)
					{
						continue;
					}
					DataEntry dataEntry = new DataEntry
					{
						RAW = array[0],
						MAT = ushort.Parse(array[3]),
						PIX = ushort.Parse(array[4]),
						CAL_Mode = ushort.Parse(array[6]),
						TimeStamp = ushort.Parse(array[7]),
						DCO = ((array[8] != "-") ? new ushort?(ushort.Parse(array[8])) : ((ushort?)null)),
						DE = ((array[9] != "-") ? new ushort?(ushort.Parse(array[9])) : ((ushort?)null)),
						CAL_Time = ((array[10] != "-") ? new ushort?(ushort.Parse(array[10])) : ((ushort?)null)),
						Cnt_TOT = int.Parse(array[13])
					};
					if (dataEntry.CAL_Mode == 1)
					{
						dataEntry.SetVariables_CALIBRATION(dataEntry.DCO, dataEntry.DE, dataEntry.CAL_Time, dataEntry.Cnt_TOT);
						if (dataEntry.DCO == 0)
						{
							CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 0] = dataEntry.DCO0_T_picoS.Value;
						}
						if (dataEntry.DCO == 1)
						{
							CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 1] = dataEntry.DCO1_T_picoS.Value;
						}
						calibra_counter++;
					}
					if (dataEntry.CAL_Mode == 0)
					{
						if (CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 0] == 0.0 || CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 1] == 0.0)
						{
							useless_counter++;
							continue;
						}
						dataEntry.DCO0_T_picoS = CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 0];
						dataEntry.DCO1_T_picoS = CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 1];
						dataEntry.SetVariables_MEASURE(int.Parse(array[12]), int.Parse(array[11]));
						measure_counter++;
					}
					if (array.Length > 14)
					{
						dataEntry.AdjCtrl_DCO0 = ushort.Parse(array[14]);
						dataEntry.AdjCtrl_DCO1 = ushort.Parse(array[15]);
					}
					if (array.Length > 16)
					{
						dataEntry.TA_Code = ((array[20] != "-") ? new ushort?(ushort.Parse(array[20])) : ((ushort?)null));
						dataEntry.TOT_Code = ((array[21] != "-") ? new ushort?(ushort.Parse(array[21])) : ((ushort?)null));
					}
					Add(dataEntry);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("Error reading file: " + ex.Message);
			}
		}

		public void AddDataFromRaw(ulong dataFifoRaw, MainForm form)
		{
			object[] array = form.RawDataToObjArray(dataFifoRaw);
			DataEntry dataEntry = new DataEntry
			{
				RAW = dataFifoRaw.ToString("X16"),
				MAT = Convert.ToUInt16(array[3]),
				PIX = Convert.ToUInt16(array[4]),
				CAL_Mode = Convert.ToUInt16(array[6]),
				TimeStamp = Convert.ToUInt16(array[7]),
				DCO = ((array[8].ToString() != "-") ? new ushort?(Convert.ToUInt16(array[8])) : ((ushort?)null)),
				DE = ((array[9].ToString() != "-") ? new ushort?(Convert.ToUInt16(array[9])) : ((ushort?)null)),
				CAL_Time = ((array[10].ToString() != "-") ? new ushort?(Convert.ToUInt16(array[10])) : ((ushort?)null)),
				Cnt_TOT = Convert.ToInt32(array[13])
			};
			if (dataEntry.CAL_Mode == 1)
			{
				dataEntry.SetVariables_CALIBRATION(dataEntry.DCO, dataEntry.DE, dataEntry.CAL_Time, dataEntry.Cnt_TOT);
				if (dataEntry.DCO == 0)
				{
					CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 0] = dataEntry.DCO0_T_picoS.Value;
				}
				if (dataEntry.DCO == 1)
				{
					CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 1] = dataEntry.DCO1_T_picoS.Value;
				}
				calibra_counter++;
			}
			if (dataEntry.CAL_Mode == 0)
			{
				if (CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 0] == 0.0 || CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 1] == 0.0)
				{
					useless_counter++;
				}
				dataEntry.DCO0_T_picoS = CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 0];
				dataEntry.DCO1_T_picoS = CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 1];
				dataEntry.SetVariables_MEASURE(Convert.ToInt32(array[12]), Convert.ToInt32(array[11]));
				measure_counter++;
			}
			Add(dataEntry);
		}

		public void AddDataFromRaw(ulong dataFifoRaw, MainForm form, ushort adjCtrlDCO0, ushort adjCtrlDCO1, bool new_cal = false, int TA_c = 999, int TOT_c = 999)
		{
			object[] array = form.RawDataToObjArray(dataFifoRaw);
			int num = Convert.ToInt32(array[3]);
			int num2 = Convert.ToInt32(array[4]);
			ushort value = adjCtrlDCO0;
			ushort value2 = adjCtrlDCO1;
			if (Resolution_Matrix[Cur_Quad, num, num2] != 0.0 && !new_cal)
			{
				value = (ushort)DCO_ConfPairs_Matrix[Cur_Quad, num, num2, 0];
				value2 = (ushort)DCO_ConfPairs_Matrix[Cur_Quad, num, num2, 1];
			}
			DataEntry dataEntry = new DataEntry
			{
				RAW = dataFifoRaw.ToString("X16"),
				MAT = Convert.ToUInt16(array[3]),
				PIX = Convert.ToUInt16(array[4]),
				CAL_Mode = Convert.ToUInt16(array[6]),
				TimeStamp = Convert.ToUInt16(array[7]),
				DCO = ((array[8].ToString() != "-") ? new ushort?(Convert.ToUInt16(array[8])) : ((ushort?)null)),
				DE = ((array[9].ToString() != "-") ? new ushort?(Convert.ToUInt16(array[9])) : ((ushort?)null)),
				CAL_Time = ((array[10].ToString() != "-") ? new ushort?(Convert.ToUInt16(array[10])) : ((ushort?)null)),
				Cnt_TOT = Convert.ToInt32(array[13]),
				AdjCtrl_DCO0 = value,
				AdjCtrl_DCO1 = value2
			};
			if (dataEntry.CAL_Mode == 1)
			{
				dataEntry.SetVariables_CALIBRATION(dataEntry.DCO, dataEntry.DE, dataEntry.CAL_Time, dataEntry.Cnt_TOT);
				if (dataEntry.DCO == 0)
				{
					CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 0] = dataEntry.DCO0_T_picoS.Value;
				}
				if (dataEntry.DCO == 1)
				{
					CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 1] = dataEntry.DCO1_T_picoS.Value;
				}
				calibra_counter++;
			}
			if (dataEntry.CAL_Mode == 0)
			{
				if (CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 0] == 0.0 || CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 1] == 0.0)
				{
					useless_counter++;
				}
				dataEntry.DCO0_T_picoS = CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 0];
				dataEntry.DCO1_T_picoS = CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 1];
				dataEntry.SetVariables_MEASURE(Convert.ToInt32(array[12]), Convert.ToInt32(array[11]));
				measure_counter++;
			}
			if (TA_c != 999)
			{
				dataEntry.TA_Code = (ushort)TA_c;
			}
			if (TOT_c != 999)
			{
				dataEntry.TOT_Code = (ushort)TOT_c;
			}
			Add(dataEntry);
		}

		public void ClearAllData()
		{
			List<DataEntry> allEntriesInOrder = GetAllEntriesInOrder();
			CAL_DCO.Clear();
			MAT_PIX.Clear();
			Custom_Entries.Clear();
			N_Stored_Entries = 0;
			measure_counter = 0;
			calibra_counter = 0;
			useless_counter = 0;
			foreach (DataEntry item in allEntriesInOrder)
			{
				item.Dispose();
			}
			allEntriesInOrder.Clear();
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}

		public void ClearCALMatrix()
		{
			for (int i = 0; i < 4; i++)
			{
				for (int j = 0; j < 16; j++)
				{
					for (int k = 0; k < 64; k++)
					{
						Resolution_Matrix[i, j, k] = 0.0;
						for (int l = 0; l < 2; l++)
						{
							CAL_Matrix[i, j, k, l] = 0.0;
							DCO_ConfPairs_Matrix[i, j, k, l] = 0;
						}
					}
				}
			}
		}
	}

	public class FIFO_like_Array<T>
	{
		private List<T> _elements;

		private int _maxSize;

		public T this[int index]
		{
			get
			{
				if (index < 0 || index >= _elements.Count)
				{
					throw new IndexOutOfRangeException("Index out of range.");
				}
				return _elements[index];
			}
			set
			{
				if (index < 0 || index >= _elements.Count)
				{
					throw new IndexOutOfRangeException("Index out of range.");
				}
				_elements[index] = value;
			}
		}

		public int Count => _elements.Count;

		public FIFO_like_Array(int maxSize)
		{
			_maxSize = maxSize;
			_elements = new List<T>();
		}

		public void Add(T item)
		{
			if (_elements.Count >= _maxSize)
			{
				_elements.RemoveAt(0);
			}
			_elements.Add(item);
		}

		public T GetOldest()
		{
			if (_elements.Count == 0)
			{
				throw new InvalidOperationException("The queue is empty.");
			}
			T result = _elements[0];
			_elements.RemoveAt(0);
			return result;
		}

		public void Clear()
		{
			_elements.Clear();
		}
	}

	private static bool TCP_ON = false;

	private static readonly object i2cLock = new object();

	public static bool debug = true;

	public SerialPort myport;

	public int FIFTEEN = 15;

	public static int Cur_Quad = 0;

	private readonly List<DaqFifoForm> _openDaqForms = new List<DaqFifoForm>();

	private readonly object _openDaqFormsLock = new object();

	public static DataIndex LoadedData = new DataIndex();

	private IContainer components;

	private MenuStrip menuStrip1;

	private ToolStripMenuItem fileToolStripMenuItem;

	private ToolStripMenuItem i2cToolsToolStripMenuItem;

	private ToolStripMenuItem infoToolStripMenuItem;

	private StatusStrip statusStrip1;

	private ToolStripStatusLabel HW_detect_tSSLabel;

	private ToolStripStatusLabel I2C_freq_tSSLabel;

	private ToolStripMenuItem saveLogToolStripMenuItem;

	private ToolStripMenuItem clearLogToolStripMenuItem;

	private ToolStripSeparator toolStripMenuItem1;

	private ToolStripMenuItem exitToolStripMenuItem;

	private SaveFileDialog saveLogFileDialog;

	private ToolStripStatusLabel I2C_oper_tSSLabel;

	private ToolStripMenuItem infoToolStripMenuItem1;

	private ToolStripMenuItem ignite0LayoutMapToolStripMenuItem;

	private ToolStripMenuItem tDCToolStripMenuItem;

	private ToolStripMenuItem TDCautoTestToolStripMenuItem;

	internal TextBox Log_textBox;

	private ToolStripMenuItem globalsToolStripMenuItem;

	private ToolStripMenuItem rowToolStripMenuItem;

	private ContextMenuStrip TDCwriteAll_contMenuStrip;

	private ToolStripMenuItem writeALLTDCsToolStripMenuItem;

	private ToolStripMenuItem writeTDCsCol0ToolStripMenuItem;

	private ToolStripMenuItem writeTDCsCol1ToolStripMenuItem;

	private ToolStripMenuItem resetToolStripMenuItem;

	private ToolStripSeparator toolStripMenuItem2;

	private ToolStripMenuItem initializingToolStripMenuItem;

	private ToolStripMenuItem externalDACsToolStripMenuItem;

	private ToolStripSeparator toolStripMenuItem3;

	private ContextMenuStrip TDCcalibAll_contMenuStrip;

	private ToolStripMenuItem calibALLTDCToolStripMenuItem;

	private ToolStripMenuItem calibCol0TDCsToolStripMenuItem;

	private ToolStripMenuItem calibCol1TDCsToolStripMenuItem;

	private ToolStripMenuItem setI2CFrequencyToolStripMenuItem;

	private ToolStripMenuItem kHz100ToolStripMenuItem;

	private ToolStripMenuItem kHz500ToolStripMenuItem;

	private ToolStripMenuItem mHzToolStripMenuItem;

	private ToolStripMenuItem tDCDataRawToolStripMenuItem;

	private ToolStripMenuItem selectDevicesToolStripMenuItem;

	private ToolStripComboBox SelDevToolStripComboBox;

	private ToolStripStatusLabel DevSerial_tSSLabel;

	private ToolTip toolTip1;

	private ToolStripMenuItem internalDACsToolStripMenuItem;

	private ToolStripSeparator toolStripMenuItem4;

	private ToolStripStatusLabel MuxStatus_tSSLabel;

	private ToolStripStatusLabel BuiltData_tSSLabel;

	private ToolStripMenuItem loadConfigToolStripMenuItem;

	private ToolStripMenuItem saveConfigToolStripMenuItem;

	private ToolStripSeparator toolStripMenuItem6;

	private OpenFileDialog openFileDialog1;

	private TabPage exAdcDac_tabPage;

	private GroupBox ExtDAC_groupBox;

	private Button DACVext_but;

	private GroupBox groupBox4;

	private Label Ext_Vinj2Scan_label;

	private NumericUpDown ScanVinj2Step_UpDown;

	private NumericUpDown ScanVinj2Min_UpDown;

	private NumericUpDown ScanVinj2Max_UpDown;

	private TextBox VDDA_txtBox;

	private GroupBox groupBox2;

	private Label Ext_ThresholdScan_label;

	private NumericUpDown ScanVthStep_UpDown;

	private NumericUpDown ScanVthMin_UpDown;

	private NumericUpDown ScanVthMax_UpDown;

	private Label Ext_Common_VDDA_label;

	private NumericUpDown DACvthr_H_UpDown;

	private Button DACvinj_H_but;

	private NumericUpDown DACvthr_L_UpDown;

	private Button DACvthr_L_but;

	private NumericUpDown DACvinj_H_UpDown;

	private Button DACvthr_H_but;

	private NumericUpDown DACVext_UpDown;

	private Label Ext_Common_VthVinjVext_label;

	private TabPage Ctrl_tabPage;

	private TabPage Mat_tabPage;

	private GroupBox MAT_cfg_groupBox;

	private Label CAL_TIME_label;

	private NumericUpDown SEL_CAL_TIME_UpDown;

	private CheckBox TDC_ON_chkBox;

	private CheckBox EN_TIMEOUT_chkBox;

	private CheckBox enDEtot_chkBox;

	private CheckBox CAL_MODE_chkBox;

	private GroupBox PIX_groupBox;

	private GroupBox MAT_COMMANDS_groupBox;

	private Button DCOcalib_but;

	private Button MatI2C_read_all_but;

	private Button MatI2C_write_all_but;

	private Button MatI2C_read_single_but;

	private Button MatI2C_write_single_but;

	private DataGridViewTextBoxColumn Row_Col1;

	private DataGridViewTextBoxColumn Row_Col2;

	private DataGridViewTextBoxColumn Row_Col3;

	private DataGridViewTextBoxColumn Row_Col4;

	private DataGridViewTextBoxColumn Row_Col5;

	private DataGridViewTextBoxColumn Row_Col6;

	private DataGridViewTextBoxColumn Row_Col7;

	private DataGridViewTextBoxColumn Row_Col8;

	private DataGridViewTextBoxColumn Row_Col9;

	private DataGridViewTextBoxColumn Row_Col10;

	private DataGridViewTextBoxColumn Row_Col11;

	private DataGridViewTextBoxColumn Row_Col12;

	private DataGridViewTextBoxColumn Row_Col13;

	private DataGridViewTextBoxColumn Row_Col14;

	private DataGridViewTextBoxColumn Row_Col15;

	private DataGridViewTextBoxColumn Row_Col16;

	private ComboBox MatAddr_comboBox;

	private TextBox MAT_I2C_addr_tBox;

	private TabPage Top_tabPage;

	private ComboBox TopAddr_comboBox;

	private Button TopI2C_read_all_but;

	private Button TopI2C_write_all_but;

	private Button TopI2C_read_single_but;

	private Button TopI2C_write_single_but;

	private TextBox TOP_I2C_addr_tBox;

	private DataGridView Top_dGridView;

	private DataGridViewTextBoxColumn Top_Col1;

	private DataGridViewTextBoxColumn Top_Col2;

	private DataGridViewTextBoxColumn Top_Col3;

	private DataGridViewTextBoxColumn Top_Col4;

	private DataGridViewTextBoxColumn Top_Col5;

	private DataGridViewTextBoxColumn Top_Col6;

	private DataGridViewTextBoxColumn Top_Col7;

	private DataGridViewTextBoxColumn Top_Col8;

	private DataGridViewTextBoxColumn Top_Col9;

	private DataGridViewTextBoxColumn Top_Col10;

	private DataGridViewTextBoxColumn Top_Col11;

	private DataGridViewTextBoxColumn Top_Col12;

	private DataGridViewTextBoxColumn Top_Col13;

	private DataGridViewTextBoxColumn Top_Col14;

	private DataGridViewTextBoxColumn Top_Col15;

	private DataGridViewTextBoxColumn Top_Col16;

	private TabControl tabControl1;

	private ToolStripMenuItem viewDatasheetToolStripMenuItem;

	private Button DAQreset_but;

	private Label MAT_DCO_GROUPS_label;

	private NumericUpDown PIX_Sel_UpDown;

	private Label PIX_Sel_label;

	private CheckBox PIX_ON_ALL_chkBox;

	private Label PIX_DCO_label;

	private NumericUpDown DCO_PIX_adj_UpDown;

	private NumericUpDown DCO_PIX_ctrl_UpDown;

	private Label DCO0_MatTab_label;

	private Label adj_ctrl_1_label;

	private NumericUpDown DCO0adj_UpDown;

	private NumericUpDown DCO0ctrl_UpDown;

	private CheckBox PIX_DCO_ALL_chkBox;

	private NumericUpDown DACvref_L_UpDown;

	private Button DACvref_L_but;

	private Label Ext_Common_Vref_label;

	private Label Ext_Vfeed_label;

	private NumericUpDown DACvfeed_UpDown;

	private Button DACvfeed_but;

	private GroupBox CurMonADC_groupBox;

	private ComboBox ADCcommon_ch_comboBox;

	private CheckBox ADCcommon_RDY_chkBox;

	private ComboBox ADCcommon_Gain_comboBox;

	private ComboBox ADCcommon_Res_comboBox;

	private CheckBox ADCcommon_OC_chkBox;

	private Label Ext_CommonADC_ResGain_label;

	private Label Ext_CommonADC_Val_Monitored_label;

	private Button ADCcommon_Read_but;

	private Button ADCcommon_1Shot_but;

	private Button ADCcommon_Write_but;

	private Label Ext_CommonADC_Data_label;

	private TextBox ADCcommon_DataHex_tBox;

	private TextBox ADCcommon_DataDec_tBox;

	private Label Ext_CommonADC_Config_label;

	private TextBox ADCcommon_Config_tBox;

	private Label Ext_CommonADC_HexDecV_label;

	private GroupBox Quad_ADC_grpBox;

	private Label Ext_QuadADC_Config_label;

	private TextBox ADCdac_Quad_Config_tBox;

	private TextBox ADCdac_Quad_DataDec_tBox;

	private Label Ext_QuadADC_Data_label;

	private TextBox ADCdac_Quad_DataHex_tBox;

	private Button ADCdac_Quad_Read_but;

	private Button ADCdac_Quad_1Shot_but;

	private Button ADCdac_Quad_Write_but;

	private Label Ext_QuadADC_ResGain_label;

	private Label Ext_QuadADC_Val_Monitored_label;

	private ComboBox ADCdac_Quad_Gain_comboBox;

	private ComboBox ADCdac_Quad_Res_comboBox;

	private ComboBox ADCdac_Quad_ch_comboBox;

	private CheckBox ADCdac_Quad_RDY_chkBox;

	private CheckBox ADCdac_Quad_OC_chkBox;

	private Label Ext_QuadADC_HexDecV_label;

	private TabPage IOext_tabPage;

	private ComboBox IOextAddr_comboBox;

	private TextBox IOext_I2C_addr_tBox;

	private DataGridView IOext_dGridView;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn1;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn2;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn3;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn4;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn5;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn6;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn7;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn8;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn9;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn10;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn11;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn12;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn13;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn14;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn15;

	private DataGridViewTextBoxColumn dataGridViewTextBoxColumn16;

	private Button IOextI2C_read_all_but;

	private Button IOextI2C_write_all_but;

	private Button IOextI2C_read_single_but;

	private Button IOextI2C_write_single_but;

	private GroupBox groupBox6;

	private Button IOexpRead_but;

	private Button IOexpWrite_but;

	private CheckBox ExtDacEn_chkBox;

	private CheckBox SiLol_chkBox;

	private Label IO_Board_ClockSel_label;

	private ComboBox SiClkInSrc_comboBox;

	private CheckBox SICLKOE_chkBox;

	private Label IO_Board_SelDataEnSrc_label;

	private ComboBox SelDataEnSrc_comboBox;

	private Label IO_Board_FastIN_label;

	private ComboBox FastinSrc_comboBox;

	private GroupBox I2Cmux_grpBox;

	private ToolStripMenuItem dCOTestToolStripMenuItem;

	private ToolStripMenuItem tDCTestToolStripMenuItem;

	private ToolStripMenuItem ignite32ToolStripMenuItem;

	private ToolStripMenuItem pCBSchemesToolStripMenuItem;

	private ToolStripMenuItem sI5341ToolStripMenuItem;

	private ToolStripMenuItem runWizardToolStripMenuItem;

	private ToolStripMenuItem loadConfigFileToolStripMenuItem;

	private ToolStripMenuItem debugModeToolStripMenuItem;

	private TextBox ADCcommon_Value_tBox;

	private TextBox ADCdac_Quad_Value_tBox;

	private ToolStripMenuItem aDCToolStripMenuItem;

	private ToolStripMenuItem dACToolStripMenuItem;

	private ToolStripMenuItem scanI2CBusToolStripMenuItem;

	private ToolStripSeparator toolStripSeparator1;

	private Button TestTDC_but;

	private Button CalibDCO_but;

	private Button TestDCO_but;

	private ToolStripMenuItem plotResultsToolStripMenuItem;

	private ToolStripMenuItem plotResultsToolStripMenuItem1;

	private NumericUpDown SLVS_CMM_MODE_UpDown;

	private CheckBox FASTIN_EN_chkBox;

	private CheckBox SLVS_INVRX_chkBox;

	private CheckBox SLVS_INVTX_chkBox;

	private ComboBox FE_POLARITY_comboBox;

	private ComboBox SLVS_TRM_comboBox;

	private NumericUpDown SLVS_DRV_STR_UpDown;

	private Label FE_POLARITY_label;

	private Label SLVS_TRM_label;

	private Label SLVS_DRV_STR_label;

	private Label SLVS_CMM_MODE_label;

	private GroupBox IOSetSel_groupBox;

	private Label BXID_LMT_label;

	private Label BXID_PRL_label;

	private NumericUpDown BXID_LMT_UpDown;

	private NumericUpDown BXID_PRL_UpDown;

	private GroupBox BXID_groupBox;

	private Label SEL_PULSE_SRC_label;

	private NumericUpDown POINT_TOT_UpDown;

	private NumericUpDown POINT_TA_UpDown;

	private ComboBox SEL_PULSE_SRC_comboBox;

	private GroupBox TDC_PULSE_groupBox;

	private Label POINT_TA_TOT_label;

	private Label TEST_POINT_label;

	private GroupBox GPO_OUT_SEL_groupBox;

	private Label READOUT_SER_label;

	private Label GPO_SEL_label;

	private ComboBox SER_CK_SEL_comboBox;

	private ComboBox SEL_RO_comboBox;

	private ComboBox GPO_CMOS_SEL_comboBox;

	private ComboBox GPO_SLVS_SEL_comboBox;

	private CheckBox DEFAULT_CONFIG_chkBox;

	private Label FIXED_PATTERN_label;

	private NumericUpDown FIXED_PATTERN_UpDown;

	private GroupBox DEF_CONFIG_groupBox;

	private Label AFE_UPDATE_TIME_label;

	private NumericUpDown AFE_UPDATE_TIME_UpDown;

	private NumericUpDown AFE_EN_TP_PHASE_UpDown;

	private NumericUpDown AFE_LISTEN_TIME_UpDown;

	private Label AFE_EN_TP_PHASE_label;

	private Label AFE_LISTEN_TIME_label;

	private GroupBox AFE_PULSE_groupBox;

	private CheckBox AFE_EOS_chkBox;

	private NumericUpDown AFE_TP_REPETITION_UpDown;

	private Label AFE_TP_REPETITION_label;

	private NumericUpDown AFE_TP_WIDTH_UpDown;

	private Label AFE_TP_WIDTH_label;

	private NumericUpDown AFE_TP_PERIOD_UpDown;

	private Label AFE_TP_PERIOD_label;

	private CheckBox AFE_START_TP_chkBox;

	private Label CMES_DPOL_label;

	private ComboBox CMES_DPOL_comboBox;

	private NumericUpDown CMES_SEL_WAIT_UpDown;

	private Label CMES_SEL_WAIT_label;

	private Label CMES_QPOL_label;

	private CheckBox CMES_ARST_chkBox;

	private ComboBox CMES_QPOL_comboBox;

	private CheckBox CMES_DEN_chkBox;

	private CheckBox CMES_AEN_chkBox;

	private GroupBox CAP_MEAS_groupBox;

	private NumericUpDown CMES_CC_04F_UpDown;

	private Label CMES_CC_04F_label;

	private NumericUpDown CMES_CQ_20F_UpDown;

	private Label CMES_CQ_20F_label;

	private NumericUpDown CMES_CF_20F_UpDown;

	private Label CMES_CF_20F_label;

	private Button TOP_TDCpulse_but;

	private Button TOP_DAQreset_but;

	private CheckBox TOP_COMM_START_CAL_chkBox;

	private CheckBox TOP_COMM_START_AUTO_chkBox;

	private CheckBox TOP_COMM_FRC_RST_CAL_chkBox;

	private GroupBox TOP_COMMANDS_groupBox;

	private Label DCO0_adj_ctr_MatTab_label;

	private CheckBox MAT_FE_ALL_chkBox;

	private CheckBox MAT_FE_ON_chkBox;

	private CheckBox PIX_ON_chkBox;

	private ComboBox CAL_SEL_DCO_comboBox;

	private Label MAT_CAL_SEL_DCO_label;

	private CheckBox MAT_DCO_GROUP_4863_chkBox;

	private CheckBox MAT_DCO_GROUP_3247_chkBox;

	private CheckBox MAT_DCO_GROUP_1631_chkBox;

	private CheckBox MAT_DCO_GROUP_0015_chkBox;

	private CheckBox EN_P_VINJ_chkBox;

	private Label SEL_VINJ_MUX_High_label;

	private GroupBox MAT_DAC_groupBox;

	private Label MAT_DAC_FT_label;

	private NumericUpDown MAT_DAC_FT_UpDown;

	private NumericUpDown MAT_DAC_VTH_H_UpDown;

	private CheckBox MAT_DAC_ALL_FT_chkBox;

	private NumericUpDown MAT_DAC_FT_SEL_UpDown;

	private Label MAT_DAC_FT_SEL_label;

	private CheckBox MAT_DAC_VTH_H_EN_chkBox;

	private ComboBox SEL_VINJ_MUX_High_comboBox;

	private CheckBox AFE_AUTO_chkBox;

	private CheckBox CON_PAD_chkBox;

	private CheckBox EN_P_VTH_chkBox;

	private CheckBox EN_P_VLDO_chkBox;

	private CheckBox EN_P_VFB_chkBox;

	private CheckBox EXT_DC_chkBox;

	private CheckBox AFE_LB_chkBox;

	private NumericUpDown MAT_DAC_VTH_L_UpDown;

	private CheckBox MAT_DAC_VTH_L_EN_chkBox;

	private NumericUpDown MAT_DAC_VINJ_H_UpDown;

	private CheckBox MAT_DAC_VINJ_H_EN_chkBox;

	private NumericUpDown MAT_DAC_VINJ_L_UpDown;

	private CheckBox MAT_DAC_VINJ_L_EN_chkBox;

	private NumericUpDown MAT_DAC_VLDO_UpDown;

	private CheckBox MAT_DAC_VLDO_EN_chkBox;

	private NumericUpDown MAT_DAC_VFB_UpDown;

	private CheckBox MAT_DAC_VFB_EN_chkBox;

	private GroupBox MAT_AFE_groupBox;

	private NumericUpDown DAC_IKRUM_UpDown;

	private NumericUpDown DAC_ICSA_UpDown;

	private NumericUpDown DAC_IDISC_UpDown;

	private ComboBox CH_MODE_42_comboBox;

	private ComboBox CH_MODE_41_comboBox;

	private NumericUpDown CH_SEL_41_UpDown;

	private NumericUpDown CH_SEL_42_UpDown;

	private Label CH_MODE_label;

	private Button DaqFifoForm_but;

	private ToolStripMenuItem iOExpanderToolStripMenuItem;

	private ComboBox I2CmuxAddr_comboBox;

	private TextBox Mux_I2C_addr_tBox;

	private Label Mux_I2C_CtrlReg_label;

	private TextBox CtrlReg_tBox;

	private Button Mux_I2C_read_but;

	private Button Mux_I2C_write_but;

	private PictureBox pictureBox1;

	private CheckBox SE_i2c_chkBox;

	private CheckBox NE_i2c_chkBox;

	private CheckBox SW_i2c_chkBox;

	private CheckBox NW_i2c_chkBox;

	private CheckBox ExtSelQuad_SE_chkBox;

	private CheckBox ExtSelQuad_NE_chkBox;

	private CheckBox ExtSelQuad_SW_chkBox;

	private CheckBox ExtSelQuad_NW_chkBox;

	private CheckBox MatSelQuad_SE_chkBox;

	private CheckBox MatSelQuad_NE_chkBox;

	private CheckBox MatSelQuad_SW_chkBox;

	private CheckBox MatSelQuad_NW_chkBox;

	private CheckBox TopSelQuad_SE_chkBox;

	private CheckBox TopSelQuad_NE_chkBox;

	private CheckBox TopSelQuad_SW_chkBox;

	private CheckBox TopSelQuad_NW_chkBox;

	private GroupBox Quad_DAC_grpBox;

	private TextBox textBox1;

	private Label Ext_Quad_VDDA_label;

	private GroupBox groupBox1;

	private Label Ext_Iref_label;

	private NumericUpDown DACiref_UpDown;

	private Button DACiref_but;

	private Label Ext_Icap_label;

	private NumericUpDown DACicap_UpDown;

	private Button DACicap_but;

	private ToolStripStatusLabel AnalogPowerStatus_tSSLabel;

	private CheckBox AnaPwr_chkBox;

	public DataGridView Mat_dGridView;

	private ToolStripMenuItem BusRecoveryToolStripMenuItem;

	private Label DAC_IDISC_label;

	private Label DAC_Ikrum_label;

	private Label DAC_ICSA_label;

	private TextBox MAT_DCO_Difference_textBox;

	private TextBox MAT_DCO0_Period_textBox;

	private TextBox MAT_DCO1_Period_textBox;

	private Label MAT_DCO_Period_label;

	private Label SEL_VINJ_MUX_Low_label;

	private ComboBox SEL_VINJ_MUX_Low_comboBox;

	private Button MAT_DAC_VLDO_tmp_but;

	private GroupBox groupBox3;

	private Button AllMAT_AllPIX_ON_but;

	private Button AllMAT_AllAFE_OFF_but;

	private Button AllMAT_AllAFE_ON_but;

	private Button AllMAT_AllTDC_OFF_but;

	private Button AllMAT_AllTDC_ON_but;

	private Button AllMAT_AllPIX_OFF_but;

	public SaveFileDialog saveFileDialog1;

	private Button CalDCO_save_but;

	private Button Visualize_DCOCalibration_but;

	private Label ADCcommon_NShot_label;

	private Button ADCcommon_NShot_button;

	private NumericUpDown ADCcommon_NShot_numUpDown;

	private Label ADCdac_Quad_NShot_label;

	private Button ADCdac_Quad_NShot_button;

	private NumericUpDown ADCdac_Quad_NShot_numUpDown;

	private ContextMenuStrip TOP_TDCpulse_contextMenuStrip;

	private ToolStripMenuItem TOP_TDCpulse_x32times;

	private ToolStripMenuItem TOP_TDCpulse_x50times;

	private ToolStripMenuItem TOP_TDCpulse_x64times;

	private ToolStripMenuItem TOP_TDCpulse_x100times;

	private ToolStripMenuItem TOP_TDCpulse_x128times;

	public GroupBox MAT_Test_Routines_groupBox;

	private Button TestATP_but;

	private ComboBox AFE_MAT_Sel_comboBox;

	private Label label1;

	private TextBox FIXED_PATTERN_textBox;

	public static bool tcp_ON
	{
		get
		{
			return TCP_ON;
		}
		set
		{
			TCP_ON = value;
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr LoadLibrary(string lpFileName);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern uint GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, int nSize);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern IntPtr GetModuleHandle(string path);

	[DllImport("TCPtoI2C.dll")]
	private static extern void EnableTransportTCP();

	[DllImport("TCPtoI2C.dll")]
	private static extern void EnableTransportUSB(string dllName);

	[DllImport("TCPtoI2C.dll")]
	private static extern void ConnectDeviceTCP(byte SerialNumber, string IP, ushort port);

	[DllImport("TCPtoI2C.dll")]
	public static extern int I2C_GetFrequency();

	[DllImport("TCPtoI2C.dll")]
	public static extern int GetNumberOfDevices();

	[DllImport("TCPtoI2C.dll")]
	public static extern int GetSerialNumbers(int[] SerialNumbers);

	[DllImport("TCPtoI2C.dll")]
	public static extern int SelectBySerialNumber(int SerialNumber);

	[DllImport("TCPtoI2C.dll")]
	public static extern int Get_DLL_Version();

	[DllImport("TCPtoI2C.dll")]
	public static extern byte I2C_Read(byte address, ushort nBytes, byte[] ReadData, ushort SendStop);

	[DllImport("TCPtoI2C.dll")]
	public static extern byte I2C_ReadArray(byte address, byte subaddress, ushort nBytes, byte[] ReadData);

	[DllImport("TCPtoI2C.dll")]
	public static extern byte I2C_ReadByte(byte address, byte subaddress, out byte ReadData);

	[DllImport("TCPtoI2C.dll")]
	public static extern byte I2C_ReceiveByte(byte address, out byte ReadData);

	[DllImport("TCPtoI2C.dll")]
	public static extern byte I2C_SendByte(byte address, byte SendData);

	[DllImport("TCPtoI2C.dll")]
	public static extern byte I2C_Write(byte address, ushort nBytes, byte[] WriteData, ushort SendStop);

	[DllImport("TCPtoI2C.dll")]
	public static extern byte I2C_WriteArray(byte address, byte subaddress, ushort nBytes, byte[] WriteData);

	[DllImport("TCPtoI2C.dll")]
	public static extern byte I2C_WriteByte(byte address, byte subaddress, byte dataByte);

	[DllImport("USBtoI2C32.dll")]
	public static extern int I2C_SetFrequency(int frequency);

	[DllImport("USBtoI2C32.dll")]
	public static extern int GPIO_IN();

	[DllImport("USBtoI2C32.dll")]
	public static extern byte GPIO_OUT(int OutputState);

	[DllImport("USBtoI2C32.dll")]
	public static extern int GPIO_Configure(byte PortConfiguation);

	[DllImport("USBtoI2C32.dll")]
	public static extern int I2C_BusRecovery();

	public static byte I2C_Guarded_WriteArray(byte address, byte subaddress, ushort nBytes, byte[] WriteData)
	{
		lock (i2cLock)
		{
			return I2C_WriteArray(address, subaddress, nBytes, WriteData);
		}
	}

	public static byte I2C_Guarded_ReadArray(byte address, byte subaddress, ushort nBytes, byte[] ReadData)
	{
		lock (i2cLock)
		{
			return I2C_ReadArray(address, subaddress, nBytes, ReadData);
		}
	}

	public static byte I2C_Guarded_WriteByte(byte address, byte subaddress, byte dataByte)
	{
		lock (i2cLock)
		{
			return I2C_WriteByte(address, subaddress, dataByte);
		}
	}

	public static byte I2C_Guarded_ReadByte(byte address, byte subaddress, out byte ReadData)
	{
		lock (i2cLock)
		{
			return I2C_ReadByte(address, subaddress, out ReadData);
		}
	}

	public static byte I2C_Guarded_SendByte(byte address, byte SendData)
	{
		lock (i2cLock)
		{
			return I2C_SendByte(address, SendData);
		}
	}

	public static byte I2C_Guarded_Read(byte address, ushort nBytes, byte[] ReadData, ushort SendStop)
	{
		lock (i2cLock)
		{
			return I2C_Read(address, nBytes, ReadData, SendStop);
		}
	}

	public int TopGetAddr()
	{
		return Convert.ToInt32(TOP_I2C_addr_tBox.Text, 16);
	}

	public ulong FifoReadSingle()
	{
		string text = "";
		int value = 252;
		byte b = 0;
		byte[] array = new byte[256];
		array.Initialize();
		if (!debug)
		{
			b = I2C_Guarded_ReadArray(Convert.ToByte(value), Convert.ToByte(64), 8, array);
		}
		else
		{
			Random random = new Random();
			for (int i = 0; i < 8; i++)
			{
				array[i] = Convert.ToByte(random.Next(0, 255));
			}
		}
		switch (b)
		{
		case 0:
		{
			for (int num = 7; num >= 0; num--)
			{
				text += array[num].ToString("X2");
			}
			return Convert.ToUInt64(text, 16);
		}
		case 3:
			return 0uL;
		default:
			MessageBox.Show(b + "\r\nin FifoReadSingle");
			return 0uL;
		}
	}

	public async Task<ulong[]> FifoReadNumWords(int fifo_number)
	{
		if (fifo_number < 0 || fifo_number > 23)
		{
			MessageBox.Show("ERROR: fifo_number outside bounds");
			return new ulong[0];
		}
		string[] DataRaw = new string[fifo_number + 1];
		I2C_oper_tSSLabel.Text = "Reading...";
		int devAddr = Convert.ToInt32(TOP_I2C_addr_tBox.Text, 16);
		byte b = 0;
		byte[] rBytes = new byte[256];
		ushort nbytes = (ushort)(8 + 8 * fifo_number);
		rBytes.Initialize();
		if (!debug)
		{
			b = await Task.Run(() => I2C_Guarded_ReadArray(Convert.ToByte(devAddr), Convert.ToByte(64), nbytes, rBytes));
		}
		else
		{
			Random random = new Random();
			for (int num = 0; num < 192; num++)
			{
				rBytes[num] = Convert.ToByte(random.Next(0, 255));
			}
		}
		if (b == 0)
		{
			ulong[] array = new ulong[fifo_number + 1];
			for (int num2 = 0; num2 <= fifo_number; num2++)
			{
				for (int num3 = 7; num3 >= 0; num3--)
				{
					DataRaw[num2] += rBytes[8 * num2 + num3].ToString("X2");
				}
				array[num2] = Convert.ToUInt64(DataRaw[num2], 16);
			}
			return array;
		}
		MessageBox.Show(b.ToString());
		return new ulong[0];
	}

	public async Task FifoReadNumAndAddToData(int NWords)
	{
		NWords--;
		if (NWords < 0 || NWords > 23)
		{
			MessageBox.Show("ERROR: NWords outside bounds");
			return;
		}
		ulong[] array = await FifoReadNumWords(NWords);
		if (array.Length != 0 && array[0] != 0L)
		{
			ulong[] array2 = array;
			foreach (ulong dataFifoRaw in array2)
			{
				LoadedData.AddDataFromRaw(dataFifoRaw, this);
			}
		}
	}

	public void Get_ScanRange(out decimal Vmax, out decimal Vmin, out decimal Vstep)
	{
		Vmax = ScanVthMax_UpDown.Value;
		Vmin = ScanVthMin_UpDown.Value;
		Vstep = ScanVthStep_UpDown.Value;
	}

	public void Get_Vinj2ScanRange(out decimal Vmax, out decimal Vmin, out decimal Vstep)
	{
		Vmax = ScanVinj2Max_UpDown.Value;
		Vmin = ScanVinj2Min_UpDown.Value;
		Vstep = ScanVinj2Step_UpDown.Value;
	}

	public void Set_DacExtVth(decimal value)
	{
		DACvthr_H_UpDown.Value = value;
		WriteDac_Click(DACvthr_H_but, EventArgs.Empty);
	}

	public void Set_DacExtVbl(decimal value)
	{
		DACvthr_L_UpDown.Value = value;
		WriteDac_Click(DACvthr_L_but, EventArgs.Empty);
	}

	public void Set_DacExtVinj(decimal value)
	{
		DACvinj_H_UpDown.Value = value;
		WriteDac_Click(DACvinj_H_but, EventArgs.Empty);
	}

	public void Set_DacExtVdump(decimal value)
	{
		DACVext_UpDown.Value = value;
		WriteDac_Click(DACVext_but, EventArgs.Empty);
	}

	public void Set_DacExtVbias(decimal value)
	{
		DACvfeed_UpDown.Value = value;
		WriteDac_Click(DACvfeed_but, EventArgs.Empty);
	}

	public void Set_DacExtVref(decimal value)
	{
		DACvref_L_UpDown.Value = value;
		WriteDac_Click(DACvref_L_but, EventArgs.Empty);
	}

	public decimal Get_DacExtVth()
	{
		return DACvthr_H_UpDown.Value;
	}

	public decimal Get_DacExtVbl()
	{
		return DACvthr_L_UpDown.Value;
	}

	public decimal Get_DacExtVinj()
	{
		return DACvinj_H_UpDown.Value;
	}

	public decimal Get_DacExtVdump()
	{
		return DACVext_UpDown.Value;
	}

	public decimal Get_DacExtVbias()
	{
		return DACvfeed_UpDown.Value;
	}

	public decimal Get_DacExtVref()
	{
		return DACvref_L_UpDown.Value;
	}

	public MainForm()
	{
		Get_DLL_Version();
		IntPtr moduleHandle = GetModuleHandle("USBtoI2C32.dll");
		StringBuilder stringBuilder = new StringBuilder(260);
		GetModuleFileName(moduleHandle, stringBuilder, stringBuilder.Capacity);
		Console.WriteLine("DLL Path: " + stringBuilder);
		Console.WriteLine(Get_DLL_Version());
		InitializeComponent();
		CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-UK");
		Top_dGridView.RowHeadersWidth = 50;
		for (int i = 0; i <= FIFTEEN; i++)
		{
			Top_dGridView.Rows.Add();
			Top_dGridView.Rows[i].Height = 19;
			Top_dGridView.Rows[i].HeaderCell.Value = i.ToString("X") + "0";
			Top_dGridView.Rows[i].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
			for (int j = 0; j < 16; j++)
			{
				if ((16 * i + j >= 19 && 16 * i + j <= 31) || (16 * i + j >= 33 && 16 * i + j <= 63))
				{
					Top_dGridView.Rows[i].Cells[j].Style.BackColor = Color.LightGray;
					Top_dGridView.Rows[i].Cells[j].ReadOnly = true;
				}
				else if (16 * i + j == 14)
				{
					Top_dGridView.Rows[i].Cells[j].Style.BackColor = Color.OrangeRed;
					Top_dGridView.Rows[i].Cells[j].Value = "00";
				}
				else if (16 * i + j == 32)
				{
					Top_dGridView.Rows[i].Cells[j].Style.BackColor = Color.LightCyan;
					Top_dGridView.Rows[i].Cells[j].Value = "00";
				}
				else if (16 * i + j >= 64 && 16 * i + j <= 255)
				{
					Top_dGridView.Rows[i].Cells[j].Style.BackColor = Color.LightGoldenrodYellow;
					Top_dGridView.Rows[i].Cells[j].Value = "00";
					Top_dGridView.Rows[i].Cells[j].ReadOnly = true;
				}
				else
				{
					Top_dGridView.Rows[i].Cells[j].Value = "00";
				}
			}
		}
		Top_dGridView.Rows[0].Cells[0].Value = "DC";
		Top_dGridView.Rows[0].Cells[2].Value = "D0";
		Top_dGridView.Rows[0].Cells[3].Value = "EB";
		Mat_dGridView.RowHeadersWidth = 50;
		AFE_MAT_Sel_comboBox.SelectedIndex = 0;
		for (int k = 0; k < 8; k++)
		{
			Mat_dGridView.Rows.Add();
			Mat_dGridView.Rows[k].Height = 19;
			Mat_dGridView.Rows[k].HeaderCell.Value = k.ToString("X") + "0";
			Mat_dGridView.Rows[k].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
			for (int l = 0; l < 16; l++)
			{
				if (k * 16 + l <= 63)
				{
					Mat_dGridView.Rows[k].Cells[l].Value = "1F";
					Mat_dGridView.Rows[k].Cells[l].ToolTipText = $"Settings PIXEL_{k * 16 + l:D2}";
				}
				else if (k * 16 + l == 64)
				{
					Mat_dGridView.Rows[k].Cells[l].Value = "10";
					Mat_dGridView.Rows[k].Cells[l].ToolTipText = "Global TDC Settings";
				}
				else if (k * 16 + l == 65 || k * 16 + l == 66)
				{
					Mat_dGridView.Rows[k].Cells[l].Value = "00";
					Mat_dGridView.Rows[k].Cells[l].ToolTipText = "Select Test Mode CH Mode/Sel";
				}
				else if (k * 16 + l >= 67 && k * 16 + l <= 69)
				{
					Mat_dGridView.Rows[4].Cells[3].Value = "83";
					Mat_dGridView.Rows[4].Cells[3].ToolTipText = "Global Settings for Analog & TDC";
					Mat_dGridView.Rows[k].Cells[l].Value = "00";
					Mat_dGridView.Rows[k].Cells[l].ToolTipText = "Global Settings for Analog";
				}
				else if (k * 16 + l >= 70 && k * 16 + l <= 75)
				{
					if (k * 16 + l == 70 || k * 16 + l == 71)
					{
						Mat_dGridView.Rows[k].Cells[l].Value = "7F";
					}
					else if (k * 16 + l == 74 || k * 16 + l == 75)
					{
						Mat_dGridView.Rows[k].Cells[l].Value = "3F";
					}
					else
					{
						Mat_dGridView.Rows[k].Cells[l].Value = "00";
					}
					Mat_dGridView.Rows[k].Cells[l].ToolTipText = "Settings for Internal DAC";
				}
				else if (k * 16 + l >= 76 && k * 16 + l <= 107)
				{
					Mat_dGridView.Rows[k].Cells[l].Value = "77";
					Mat_dGridView.Rows[k].Cells[l].ToolTipText = $"Settings for Fine Tune DAC PX_{(k * 16 + l - 76) * 2:D2} & {(k * 16 + l - 76) * 2 + 1:D2}";
				}
				else if (k * 16 + l >= 108 && k * 16 + l <= 111)
				{
					Mat_dGridView.Rows[k].Cells[l].Value = "";
					Mat_dGridView.Rows[k].Cells[l].Style.BackColor = Color.LightGray;
					Mat_dGridView.Rows[k].Cells[l].ReadOnly = true;
				}
				else if (k * 16 + l == 112)
				{
					Mat_dGridView.Rows[k].Cells[l].Style.BackColor = Color.LightCyan;
					Mat_dGridView.Rows[k].Cells[l].Value = "00";
					Mat_dGridView.Rows[k].Cells[l].ToolTipText = "COMMANDS";
				}
				else
				{
					Mat_dGridView.Rows[k].Cells[l].Style.BackColor = Color.LightGray;
					Mat_dGridView.Rows[k].Cells[l].ReadOnly = true;
				}
			}
		}
		IOext_dGridView.RowHeadersWidth = 50;
		for (int m = 0; m <= 0; m++)
		{
			IOext_dGridView.Rows.Add();
			IOext_dGridView.Rows[m].Height = 19;
			IOext_dGridView.Rows[m].HeaderCell.Value = m.ToString("X") + "0";
			IOext_dGridView.Rows[m].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
			for (int n = 0; n < 16; n++)
			{
				if (n > 10)
				{
					IOext_dGridView.Rows[m].Cells[n].Style.BackColor = Color.LightGray;
					IOext_dGridView.Rows[m].Cells[n].ReadOnly = true;
					continue;
				}
				IOext_dGridView.Rows[m].Cells[n].Value = "00";
				switch (n)
				{
				case 0:
					IOext_dGridView.Rows[m].Cells[n].ToolTipText = "I/O DIRECTION REGISTER: \n\t1 = Pin is configured as an input";
					break;
				case 1:
					IOext_dGridView.Rows[m].Cells[n].ToolTipText = "INPUT POLARITY REGISTER: \n\t1 = GPIO register bit will reflect the opposite logic state of the input pin";
					break;
				case 2:
					IOext_dGridView.Rows[m].Cells[n].ToolTipText = "INTERRUPT-ON-CHANGE CONTROL REGISTER";
					break;
				case 3:
					IOext_dGridView.Rows[m].Cells[n].ToolTipText = "DEFAULT COMPARE REGISTER FOR INTERRUPT-ON-CHANGE";
					break;
				case 4:
					IOext_dGridView.Rows[m].Cells[n].ToolTipText = "INTERRUPT CONTROL REGISTER";
					break;
				case 5:
					IOext_dGridView.Rows[m].Cells[n].ToolTipText = "CONFIGURATION REGISTER:\n bit 5 SEQOP: Sequential Operation mode bit. \n\t1 = Sequential operation disabled, address pointer does not increment\n bit 2 ODR: Configures the INT pin as an open-drain output.\n bit 1 INTPOL: Sets the polarity of the INT output pin.\n bit 0 INTCC: Interrupt Clearing Control";
					break;
				case 6:
					IOext_dGridView.Rows[m].Cells[n].ToolTipText = "PULL-UP RESISTOR CONFIGURATION REGISTER:\n\t1 = Pull-Up enabled";
					break;
				case 7:
					IOext_dGridView.Rows[m].Cells[n].ToolTipText = "INTERRUPT FLAG REGISTER";
					break;
				case 8:
					IOext_dGridView.Rows[m].Cells[n].ToolTipText = "INTERRUPT CAPTURE REGISTER";
					break;
				case 9:
					IOext_dGridView.Rows[m].Cells[n].ToolTipText = "PORT REGISTER:\nThe GPIO register reflects the value on the port. Reading from this register reads the port.\nWriting to this register modifies the Output Latch(OLAT) register.";
					break;
				case 10:
					IOext_dGridView.Rows[m].Cells[n].ToolTipText = "OUTPUT LATCH REGISTER (OLAT)The OLAT register provides access to the output latches.\nA read from this register results in a read of the OLAT and not the port itself.\nA write to this register modifies the output latches that modifies the pins configured as outputs.";
					break;
				}
			}
		}
		TopAddr_comboBox.SelectedIndex = 0;
		MatAddr_comboBox.SelectedIndex = 0;
		IOextAddr_comboBox.SelectedIndex = 0;
		I2CmuxAddr_comboBox.SelectedIndex = 0;
		IOSetSel_refresh();
		BXID_refresh();
		TDC_PULSE_refresh();
		GPO_OUT_SEL_refresh();
		DEF_CONFIG_refresh();
		AFE_PULSE_refresh();
		CAP_MEAS_refresh();
		TOP_COMMANDS_refresh();
		config_refresh();
		PIXset_refresh();
		MAT_DACset_refresh();
		MAT_AFE_refresh();
		IOext_gpio_refresh();
		I2Cmux_refresh();
		debugModeToolStripMenuItem.Checked = debug;
	}

	private void MainForm_Load(object sender, EventArgs e)
	{
		using (StartupTransportSelectionForm startupTransportSelectionForm = new StartupTransportSelectionForm(this))
		{
			if (startupTransportSelectionForm.ShowDialog() == DialogResult.OK)
			{
				if (TCP_ON)
				{
					string text = startupTransportSelectionForm.IP_Address;
					ushort port = 7;
					MessageBox.Show("Hai scelto protocollo TCP, con IP :  " + text);
					if (text.Contains(":"))
					{
						string[] array = text.Split(':');
						text = array[0];
						if (array.Length > 1 && ushort.TryParse(array[1], out var result))
						{
							port = result;
						}
					}
					EnableTransportTCP();
					ConnectDeviceTCP(Convert.ToByte(2), text, port);
				}
				else
				{
					EnableTransportUSB("USBtoI2C32.dll");
				}
			}
		}
		int[] array2 = new int[10];
		DateTime lastWriteTime = File.GetLastWriteTime("./tb_Ignite64.exe");
		BuiltData_tSSLabel.Text = "Build Data: " + lastWriteTime.ToString();
		Log_textBox.AppendText(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Get_DLL_Version();
		Task.Delay(250).Wait();
		if (GetSerialNumbers(array2) > 0)
		{
			infoToolStripMenuItem_Click(sender, e);
			if (SelectBySerialNumber(array2[0]) == 0 && !TCP_ON)
			{
				MessageBox.Show("USB to I2C Elite hardware not found");
				Log_textBox.AppendText("==> USB to I2C Elite hardware not found \r\n");
				Close();
			}
			else if (TCP_ON)
			{
				HW_detect_tSSLabel.Text = "TCP Mode ON";
				GPIO_Configure(Convert.ToByte(15));
				GPIO_OUT(10);
				Log_textBox.AppendText("==> USB to I2C Elite detected\r\n");
				IOext_init();
				Log_textBox.AppendText("==> I/O extender initialized\r\n");
			}
			else
			{
				HW_detect_tSSLabel.Text = "USB to I2C Elite detected";
				I2C_SetFrequency(500000);
				I2C_freq_tSSLabel.Text = "I2C freq: " + Convert.ToString(I2C_GetFrequency() / 1000) + " kHz";
				GPIO_Configure(Convert.ToByte(15));
				GPIO_OUT(10);
				Log_textBox.AppendText("==> USB to I2C Elite detected\r\n");
				IOext_init();
				Log_textBox.AppendText("==> I/O extender initialized\r\n");
			}
		}
		else
		{
			MessageBox.Show("Una mazza!!!!!!!!!!!!  " + GetSerialNumbers(array2));
		}
	}

	private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
	{
		DialogResult dialogResult = MessageBox.Show("Do you really want to exit?", "Are you sure?", MessageBoxButtons.YesNo);
		e.Cancel = dialogResult == DialogResult.No;
	}

	private void gpio_dac(int cmd, int addr, int value)
	{
		Log_textBox.AppendText(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		GPIO_OUT(8);
		for (int num = 3; num >= 0; num--)
		{
			if (Convert.ToBoolean((cmd >> num) & 1))
			{
				GPIO_OUT(24);
			}
			else
			{
				GPIO_OUT(8);
			}
			GPIO_OUT(GPIO_IN() | 0x28);
			GPIO_OUT(GPIO_IN() & 0x18);
		}
		for (int num2 = 3; num2 >= 0; num2--)
		{
			if (Convert.ToBoolean((addr >> num2) & 1))
			{
				GPIO_OUT(24);
			}
			else
			{
				GPIO_OUT(8);
			}
			GPIO_OUT(GPIO_IN() | 0x28);
			GPIO_OUT(GPIO_IN() & 0x18);
		}
		for (int num3 = 15; num3 >= 0; num3--)
		{
			if (Convert.ToBoolean((value >> num3) & 1))
			{
				GPIO_OUT(24);
			}
			else
			{
				GPIO_OUT(8);
			}
			GPIO_OUT(GPIO_IN() | 0x28);
			GPIO_OUT(GPIO_IN() & 0x18);
		}
		GPIO_OUT(10);
		GPIO_OUT(42);
		GPIO_OUT(10);
		Log_textBox.AppendText(" ==> DAC [" + addr + "] Written with Code [" + value + "]\r\n");
	}

	private void writeSI5340ConfFromFile(string SIConfFileName)
	{
		string[] separator = new string[3] { " ", ",", "0x" };
		byte b = 0;
		StreamReader streamReader = new StreamReader(SIConfFileName);
		Log_textBox.AppendText(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Log_textBox.AppendText(" => Loading SI5340 Configuration \r\n");
		while (!streamReader.EndOfStream)
		{
			string text = streamReader.ReadLine();
			if (!(text != "") || b != 0)
			{
				continue;
			}
			string[] array = text.Split(separator, StringSplitOptions.RemoveEmptyEntries);
			if (array[0].Equals("#") && array.Length > 4 && array[1].Equals("Created"))
			{
				Log_textBox.AppendText("\r\t " + array[1] + " " + array[2] + " " + array[3] + " " + array[4] + "\r\n");
			}
			else if (array[0].Equals("#") && array.Length > 3 && array[1].Equals("Delay") && array[3].Equals("msec"))
			{
				Task.Delay(Convert.ToInt32(array[2])).Wait();
			}
			else
			{
				if (array.Length != 2 || array[0].Equals("Address"))
				{
					continue;
				}
				int value = Convert.ToInt32(array[0].Substring(0, 2), 16);
				int value2 = Convert.ToInt32(array[0].Substring(2), 16);
				byte value3 = Convert.ToByte(Convert.ToInt32(array[1], 16));
				if (!debug)
				{
					b = I2C_Guarded_WriteByte(Convert.ToByte(234), Convert.ToByte(1), Convert.ToByte(value));
					if (b != 0)
					{
						MessageBox.Show(b + ": \r\nError setting Page Address " + value);
					}
					b = I2C_Guarded_WriteByte(Convert.ToByte(234), Convert.ToByte(value2), Convert.ToByte(value3));
					if (b != 0)
					{
						MessageBox.Show(b + ": \r\nError writting Conf Reg " + value2 + " with val " + value3);
					}
				}
			}
		}
		Log_textBox.AppendText(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Log_textBox.AppendText(" => SI5340 Configured \r\n");
	}

	private string saveFullConfigurationToString()
	{
		string[] array = new string[4] { "SW", "NW", "SE", "NE" };
		string text = "Quadrant " + array[Cur_Quad] + "\n" + Cur_Quad + "\n\nTOP\n";
		Log_textBox.AppendText("\r\nReading TOP Configuration...");
		byte ReadData;
		for (int i = 0; i < 19; i++)
		{
			byte address = Convert.ToByte(252);
			byte b = I2C_Guarded_ReadByte(address, Convert.ToByte(i), out ReadData);
			if (b != 0)
			{
				MessageBox.Show("Error while reading TOP Registers:\n" + b);
				return "";
			}
			text = text + ReadData + "\n";
		}
		Log_textBox.AppendText("  DONE!");
		for (int j = 0; j < 16; j++)
		{
			if (j > 3 && j < 8)
			{
				continue;
			}
			Log_textBox.AppendText($"\r\nReading MAT {j} Configuration ...");
			text = text + "\n\nMAT " + j.ToString().PadRight(2) + "\n";
			for (int k = 0; k < 108; k++)
			{
				byte address2 = Convert.ToByte(2 * j);
				byte b = I2C_Guarded_ReadByte(address2, Convert.ToByte(k), out ReadData);
				if (b != 0)
				{
					MessageBox.Show("Error while reading MAT Registers:\n" + b);
					return "";
				}
				text = text + ReadData + "\n";
			}
			Log_textBox.AppendText("  DONE!");
		}
		text += "\n\nI/O Ext & I2C Mux Registers\n";
		Log_textBox.AppendText("\r\nReading I/O Ext & I2C Mux Configuration...");
		for (int l = 0; l < 11; l++)
		{
			byte address3 = Convert.ToByte(64);
			byte b = I2C_Guarded_ReadByte(address3, Convert.ToByte(l), out ReadData);
			if (b != 0)
			{
				MessageBox.Show("Error while reading TOP Registers:\n" + b);
				return "";
			}
			text = text + ReadData + "\n";
		}
		Log_textBox.AppendText("  DONE!");
		return text;
	}

	private bool loadFullConfigurationFromFile(string filePath)
	{
		string[] array = File.ReadAllLines(filePath);
		int i;
		for (i = 0; i < array.Length && string.IsNullOrWhiteSpace(array[i]); i++)
		{
		}
		for (; i < array.Length && !array[i].StartsWith("TOP"); i++)
		{
		}
		i++;
		Log_textBox.AppendText("\r\nLoading TOP Configuration...");
		int num = 0;
		while (num < 19 && i < array.Length)
		{
			if (!byte.TryParse(array[i].Trim(), out var result))
			{
				return false;
			}
			byte address = 252;
			if (I2C_Guarded_WriteByte(address, (byte)num, result) != 0)
			{
				return false;
			}
			num++;
			i++;
		}
		Log_textBox.AppendText("  DONE!");
		for (int j = 0; j < 16; j++)
		{
			if (j > 3 && j < 8)
			{
				continue;
			}
			for (; i < array.Length && string.IsNullOrWhiteSpace(array[i]); i++)
			{
			}
			for (; i < array.Length && !array[i].StartsWith("MAT"); i++)
			{
			}
			i++;
			Log_textBox.AppendText($"\r\nLoading MAT {j} Configuration ...");
			int num2 = 0;
			while (num2 < 108 && i < array.Length)
			{
				if (!byte.TryParse(array[i].Trim(), out var result2))
				{
					return false;
				}
				byte address2 = (byte)(2 * j);
				if (I2C_Guarded_WriteByte(address2, (byte)num2, result2) != 0)
				{
					return false;
				}
				num2++;
				i++;
			}
			Log_textBox.AppendText("  DONE!");
		}
		for (; i < array.Length && string.IsNullOrWhiteSpace(array[i]); i++)
		{
		}
		for (; i < array.Length && !array[i].StartsWith("I/O Ext"); i++)
		{
		}
		i++;
		Log_textBox.AppendText("\r\nLoading I/O Ext & I2C Mux Configuration...");
		int num3 = 0;
		while (num3 < 11 && i < array.Length)
		{
			if (!byte.TryParse(array[i].Trim(), out var result3))
			{
				return false;
			}
			byte address3 = 64;
			if (I2C_Guarded_WriteByte(address3, (byte)num3, result3) != 0)
			{
				return false;
			}
			num3++;
			i++;
		}
		Log_textBox.AppendText("  DONE!");
		return true;
	}

	private void loadConfigToolStripMenuItem_Click(object sender, EventArgs e)
	{
		openFileDialog1.Filter = "Text File|*.txt|All Files|*.*";
		openFileDialog1.Title = "Open a configuration file";
		if (openFileDialog1.ShowDialog() == DialogResult.OK)
		{
			Log_textBox.AppendText("/r/nLoading Full Configuration from " + openFileDialog1.FileName + "/r/n");
			if (loadFullConfigurationFromFile(openFileDialog1.FileName))
			{
				MessageBox.Show("Configuration Loaded Successfully");
				Log_textBox.AppendText("/r/nFull Configuration successfully loaded from " + openFileDialog1.FileName + "/r/n");
			}
			else
			{
				MessageBox.Show("Could not load configuration\n :(");
				Log_textBox.AppendText("/r/nFAILED to load Full Configuration from " + openFileDialog1.FileName + "/r/n");
			}
		}
	}

	private void saveConfigToolStripMenuItem_Click(object sender, EventArgs e)
	{
		string[] array = new string[4] { "SW", "NW", "SE", "NE" };
		string fileName = "IGNITE64_config" + array[Cur_Quad] + DateTime.Now.ToString("_yy.MM.dd.HH.mm.ss");
		string text = "";
		saveFileDialog1.FileName = fileName;
		if (saveFileDialog1.ShowDialog() == DialogResult.OK)
		{
			Log_textBox.AppendText("\r\nStarting save of full configuration as " + saveFileDialog1.FileName + "\r\n");
			text = saveFullConfigurationToString();
			using (StreamWriter streamWriter = new StreamWriter(saveFileDialog1.FileName))
			{
				streamWriter.WriteLine(text);
				streamWriter.Close();
			}
			Log_textBox.AppendText("\r\nFull Configuration saved as " + saveFileDialog1.FileName + "\r\n");
		}
	}

	private void saveLogToolStripMenuItem_Click(object sender, EventArgs e)
	{
		if (saveLogFileDialog.ShowDialog() == DialogResult.OK)
		{
			using (StreamWriter streamWriter = new StreamWriter(saveLogFileDialog.FileName))
			{
				streamWriter.WriteLine(Log_textBox.Text);
				streamWriter.Close();
			}
		}
	}

	private void clearLogToolStripMenuItem_Click(object sender, EventArgs e)
	{
		DialogResult dialogResult = MessageBox.Show("Are you sure to clear the log window?", "Confirm Clear Log", MessageBoxButtons.OKCancel);
		if (dialogResult == DialogResult.OK)
		{
			Log_textBox.Clear();
		}
	}

	private void exitToolStripMenuItem_Click(object sender, EventArgs e)
	{
		Close();
	}

	private void infoToolStripMenuItem_Click(object sender, EventArgs e)
	{
		int[] array = new int[10];
		string text = "";
		string text2 = "";
		int num = Get_DLL_Version();
		do
		{
			int num2 = num % 10;
			num /= 10;
			text2 = Convert.ToString(num2) + "." + text2;
		}
		while (num != 0);
		SelDevToolStripComboBox.Items.Clear();
		text = text + "Number of devices: " + Convert.ToString(GetSerialNumbers(array)) + "\n";
		for (int i = 0; i < GetSerialNumbers(array); i++)
		{
			text = text + "Serial device [" + i + "]: " + Convert.ToString(array[i]) + "\n";
			SelDevToolStripComboBox.Items.Insert(i, Convert.ToString(array[i]));
			SelDevToolStripComboBox.SelectedIndex = 0;
		}
		text = text + "I2C frequency: " + Convert.ToString(I2C_GetFrequency()) + "\n";
		text = text + "USBtoI2C32.dll Version: " + text2 + "\n";
		MessageBox.Show(text);
	}

	private void SelDevToolStripComboBox_SelectedIndexChanged(object sender, EventArgs e)
	{
		if (SelectBySerialNumber(Convert.ToInt32(SelDevToolStripComboBox.SelectedItem)) == 0)
		{
			MessageBox.Show("USB to I2C Elite hardware not found");
			Log_textBox.AppendText("==> USB to I2C Elite hardware not found \r\n");
			Close();
			return;
		}
		HW_detect_tSSLabel.Text = "USB to I2C Elite detected";
		I2C_SetFrequency(100000);
		I2C_freq_tSSLabel.Text = "I2C freq: " + Convert.ToString(I2C_GetFrequency() / 1000) + " kHz";
		DevSerial_tSSLabel.Text = "Serial Device: " + SelDevToolStripComboBox.SelectedItem;
		GPIO_Configure(Convert.ToByte(15));
		GPIO_OUT(10);
		Log_textBox.AppendText("==> USB to I2C Elite detected\r\n");
	}

	private void setI2CFrequencyToolStripMenuItem_Click(object sender, EventArgs e)
	{
		if (sender.Equals(kHz100ToolStripMenuItem))
		{
			I2C_SetFrequency(100000);
		}
		else if (sender.Equals(kHz500ToolStripMenuItem))
		{
			I2C_SetFrequency(500000);
		}
		else if (sender.Equals(mHzToolStripMenuItem))
		{
			I2C_SetFrequency(1000000);
		}
		else if (sender.Equals(BusRecoveryToolStripMenuItem))
		{
			int num = 5;
			num = I2C_BusRecovery();
			if (num == 0)
			{
				MessageBox.Show("Bus Recovery Success");
			}
			if (num != 0)
			{
				MessageBox.Show("Bus Recovery Failed");
			}
		}
		I2C_freq_tSSLabel.Text = "I2C freq: " + Convert.ToString(I2C_GetFrequency() / 1000) + " kHz";
	}

	private void scanI2CbusToolStripMenuItem_Click(object sender, EventArgs e)
	{
		byte b = 0;
		byte[] writeData = new byte[2];
		Log_textBox.AppendText(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Log_textBox.AppendText(" => Scanning I2C bus looking for slave devices \r\n");
		for (int i = 0; i < 128; i++)
		{
			if (I2C_Guarded_WriteArray(Convert.ToByte(i << 1), 0, 0, writeData) == 0)
			{
				Log_textBox.AppendText("\r\tFound Addr [" + i + "][0x" + i.ToString("X2") + "] \r\n");
			}
		}
		Log_textBox.AppendText(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Log_textBox.AppendText(" => END of Scan \r\n");
	}

	private void debugModeToolStripMenuItem_Click(object sender, EventArgs e)
	{
		if (debug)
		{
			debug = false;
		}
		else
		{
			debug = true;
		}
		debugModeToolStripMenuItem.Checked = debug;
	}

	private void runWizardToolStripMenuItem_Click(object sender, EventArgs e)
	{
		Process.Start("ClockBuilder Pro");
	}

	private void loadSIConfigToolStripMenuItem_Click(object sender, EventArgs e)
	{
		openFileDialog1.Filter = "Text File|*.txt|All Files|*.*";
		openFileDialog1.Title = "Open a configuration file";
		if (openFileDialog1.ShowDialog() == DialogResult.OK)
		{
			writeSI5340ConfFromFile(openFileDialog1.FileName);
		}
	}

	private void resetToolStripMenuItem_Click(object sender, EventArgs e)
	{
		GPIO_OUT(11);
		GPIO_OUT(10);
		Log_textBox.AppendText(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Log_textBox.AppendText("==> Chip Reset\r\n");
	}

	private void dCOTestToolStripMenuItem_Click(object sender, EventArgs e)
	{
	}

	private void TDCautoTestToolStripMenuItem_Click(object sender, EventArgs e)
	{
	}

	private void tDCDataRawToolStripMenuItem_Click(object sender, EventArgs e)
	{
	}

	private void plotResult_Click(object sender, EventArgs e)
	{
		PlotResult plotResult = new PlotResult();
		plotResult.Show();
	}

	private void ignite32LayoutMapToolStripMenuItem_Click(object sender, EventArgs e)
	{
		ignite32Layout ignite32Layout2 = new ignite32Layout();
		ignite32Layout2.Show();
	}

	private void viewDatasheetToolStripMenuItem_Click(object sender, EventArgs e)
	{
		string fileName = "IGNITE64_datasheet_v0p2d2_small.pdf";
		string fileName2 = "Schematic Prints.pdf";
		string fileName3 = "MCP23009-MCP23S09-8-Bit-IO-Expander.pdf";
		string fileName4 = "MCP3428.pdf";
		string fileName5 = "LTC2609IGN.pdf";
		if (sender.Equals(ignite32ToolStripMenuItem))
		{
			Process.Start(fileName);
		}
		else if (sender.Equals(pCBSchemesToolStripMenuItem))
		{
			Process.Start(fileName2);
		}
		else if (sender.Equals(aDCToolStripMenuItem))
		{
			Process.Start(fileName4);
		}
		else if (sender.Equals(dACToolStripMenuItem))
		{
			Process.Start(fileName5);
		}
		else if (sender.Equals(iOExpanderToolStripMenuItem))
		{
			Process.Start(fileName3);
		}
	}

	private void TDCwriteAll_contMenuStrip_Click(object sender, EventArgs e)
	{
		int num = 0;
		byte[] array = new byte[32];
		bool flag = false;
		bool flag2 = false;
		int num2 = 0;
		string text = "";
		MatI2C_write_all_but.Enabled = false;
		if (sender.Equals(writeALLTDCsToolStripMenuItem))
		{
			flag = true;
			flag2 = true;
		}
		else if (sender.Equals(writeTDCsCol0ToolStripMenuItem))
		{
			flag = true;
		}
		else if (sender.Equals(writeTDCsCol1ToolStripMenuItem))
		{
			flag2 = true;
		}
		for (int i = 8; i <= 9; i++)
		{
			for (int j = 0; j < 16; j++)
			{
				if (16 * i + j <= 153)
				{
					array[num] = Convert.ToByte(Convert.ToInt32(Mat_dGridView.Rows[i].Cells[j].Value.ToString(), 16));
					num++;
				}
			}
		}
		if (flag)
		{
			for (int k = 32; k <= 47; k++)
			{
				if (I2C_Guarded_WriteArray(Convert.ToByte(k << 1), Convert.ToByte(128), 26, array) != 0)
				{
					text = text + "Col 0 ROW " + num2 + " not responding \r\t";
				}
				num2++;
			}
		}
		num2 = 0;
		if (flag2)
		{
			for (int k = 48; k <= 63; k++)
			{
				if (I2C_Guarded_WriteArray(Convert.ToByte(k << 1), Convert.ToByte(128), 26, array) != 0)
				{
					text = text + "Col 1 ROW " + num2 + " not responding \r\t";
				}
				num2++;
			}
		}
		if (text.Length > 0)
		{
			MessageBox.Show(text);
		}
		MatI2C_write_all_but.Enabled = true;
	}

	private void TDCcalibAll_contMenuStrip_Click(object sender, EventArgs e)
	{
		int value = 32;
		bool flag = false;
		bool flag2 = false;
		int num = 0;
		string text = "";
		DCOcalib_but.Enabled = false;
		if (sender.Equals(calibALLTDCToolStripMenuItem))
		{
			flag = true;
			flag2 = true;
		}
		else if (sender.Equals(calibCol0TDCsToolStripMenuItem))
		{
			flag = true;
		}
		else if (sender.Equals(calibCol1TDCsToolStripMenuItem))
		{
			flag2 = true;
		}
		byte ReadData;
		if (flag)
		{
			for (int i = 32; i <= 47; i++)
			{
				byte b = I2C_Guarded_ReadByte(Convert.ToByte(i << 1), Convert.ToByte(152), out ReadData);
				if (b != 0)
				{
					text = text + "Col 0 ROW " + num + " not responding reading CFG_MODE \r\t";
				}
				if (b != 0)
				{
					text = text + "Col 0 ROW " + num + " not responding for CFG_MODE\r\t";
				}
				if (I2C_Guarded_WriteByte(Convert.ToByte(i << 1), Convert.ToByte(160), Convert.ToByte(value)) != 0)
				{
					text = text + "Col 0 ROW " + num + " not responding for CMD\r\t";
				}
				num++;
			}
		}
		num = 0;
		if (flag2)
		{
			for (int i = 48; i <= 63; i++)
			{
				byte b = I2C_Guarded_ReadByte(Convert.ToByte(i << 1), Convert.ToByte(152), out ReadData);
				if (b != 0)
				{
					text = text + "Col 0 ROW " + num + " not responding reading CFG_MODE \r\t";
				}
				if (b != 0)
				{
					text = text + "Col 1 ROW " + num + " not responding for CFG_MODE\r\t";
				}
				if (I2C_Guarded_WriteByte(Convert.ToByte(i << 1), Convert.ToByte(160), Convert.ToByte(value)) != 0)
				{
					text = text + "Col 1 ROW " + num + " not responding for CMD\r\t";
				}
				num++;
			}
		}
		if (text.Length > 0)
		{
			MessageBox.Show(text);
		}
		DCOcalib_but.Enabled = true;
	}

	private void TopAddr_comboBox_SelectedIndexChanged(object sender, EventArgs e)
	{
		switch (TopAddr_comboBox.SelectedIndex)
		{
		case 0:
			TOP_I2C_addr_tBox.Text = "FC";
			TOP_I2C_addr_tBox.Enabled = false;
			break;
		case 1:
			TOP_I2C_addr_tBox.Text = "AE";
			TOP_I2C_addr_tBox.Enabled = false;
			break;
		case 2:
			TOP_I2C_addr_tBox.Text = "00";
			TOP_I2C_addr_tBox.Enabled = true;
			break;
		}
	}

	private void Top_dGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
	{
		int num = Convert.ToInt32(Top_dGridView.SelectedCells[0].Value.ToString(), 16);
		Top_dGridView.SelectedCells[0].Value = num.ToString("X2");
	}

	private void TopI2C_write_single_but_Click(object sender, EventArgs e)
	{
		I2C_oper_tSSLabel.Text = "Writing...";
		int value = Convert.ToInt32(TOP_I2C_addr_tBox.Text, 16);
		int value2 = 16 * Top_dGridView.CurrentRow.Index + Top_dGridView.CurrentCell.ColumnIndex;
		byte b = 0;
		int value3 = Convert.ToInt32(Top_dGridView.SelectedCells[0].Value.ToString(), 16);
		if (!debug)
		{
			b = I2C_Guarded_WriteByte(Convert.ToByte(value), Convert.ToByte(value2), Convert.ToByte(value3));
		}
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
		IOSetSel_refresh();
		BXID_refresh();
		TDC_PULSE_refresh();
		GPO_OUT_SEL_refresh();
		DEF_CONFIG_refresh();
		AFE_PULSE_refresh();
		CAP_MEAS_refresh();
		TOP_COMMANDS_refresh();
	}

	private void TopI2C_read_single_but_Click(object sender, EventArgs e)
	{
		I2C_oper_tSSLabel.Text = "Reading...";
		int value = Convert.ToInt32(TOP_I2C_addr_tBox.Text, 16);
		int value2 = 16 * Top_dGridView.CurrentRow.Index + Top_dGridView.CurrentCell.ColumnIndex;
		byte ReadData;
		byte b;
		if (debug)
		{
			Random random = new Random();
			ReadData = Convert.ToByte(random.Next(0, 255));
			b = 0;
		}
		else
		{
			b = I2C_Guarded_ReadByte(Convert.ToByte(value), Convert.ToByte(value2), out ReadData);
		}
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
			Top_dGridView.SelectedCells[0].Value = ReadData.ToString("X2");
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
		IOSetSel_refresh();
		BXID_refresh();
		TDC_PULSE_refresh();
		GPO_OUT_SEL_refresh();
		DEF_CONFIG_refresh();
		AFE_PULSE_refresh();
		CAP_MEAS_refresh();
		TOP_COMMANDS_refresh();
	}

	private void TopI2C_write_all_but_Click(object sender, EventArgs e)
	{
		I2C_oper_tSSLabel.Text = "Writing...";
		int value = Convert.ToInt32(TOP_I2C_addr_tBox.Text, 16);
		byte b = 0;
		byte[] array = new byte[256];
		for (int i = 0; i <= FIFTEEN; i++)
		{
			for (int j = 0; j < 16; j++)
			{
				if ((16 * i + j <= 18 || 16 * i + j >= 32) && (16 * i + j <= 32 || 16 * i + j >= 64) && 16 * i + j <= 256)
				{
					array[16 * i + j] = Convert.ToByte(Convert.ToInt32(Top_dGridView.Rows[i].Cells[j].Value.ToString(), 16));
				}
			}
		}
		if (!debug)
		{
			b = I2C_Guarded_WriteArray(Convert.ToByte(value), Convert.ToByte(0), 256, array);
		}
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
		IOSetSel_refresh();
		BXID_refresh();
		TDC_PULSE_refresh();
		GPO_OUT_SEL_refresh();
		DEF_CONFIG_refresh();
		AFE_PULSE_refresh();
		CAP_MEAS_refresh();
		TOP_COMMANDS_refresh();
	}

	private void TopI2C_read_all_but_Click(object sender, EventArgs e)
	{
		I2C_oper_tSSLabel.Text = "Reading...";
		int value = Convert.ToInt32(TOP_I2C_addr_tBox.Text, 16);
		byte b = 0;
		byte[] array = new byte[256];
		array.Initialize();
		if (!debug)
		{
			b = I2C_Guarded_ReadArray(Convert.ToByte(value), Convert.ToByte(0), 33, array);
		}
		else
		{
			Random random = new Random();
			for (int i = 0; i < 33; i++)
			{
				array[i] = Convert.ToByte(random.Next(0, 255));
			}
		}
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
			for (int j = 0; j <= 2; j++)
			{
				for (int k = 0; k < 16; k++)
				{
					if ((16 * j + k <= 18 || 16 * j + k >= 32) && (16 * j + k <= 32 || 16 * j + k >= 64) && 16 * j + k <= 256)
					{
						Top_dGridView.Rows[j].Cells[k].Value = array[16 * j + k].ToString("X2");
					}
				}
			}
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
		IOSetSel_refresh();
		BXID_refresh();
		TDC_PULSE_refresh();
		GPO_OUT_SEL_refresh();
		DEF_CONFIG_refresh();
		AFE_PULSE_refresh();
		CAP_MEAS_refresh();
		TOP_COMMANDS_refresh();
	}

	private void TopI2C_write_range(int regStart, int regStop)
	{
		if (regStart <= 161 && regStop <= 161)
		{
			I2C_oper_tSSLabel.Text = "Writing...";
			int value = Convert.ToInt32(TOP_I2C_addr_tBox.Text, 16);
			byte b = 0;
			byte[] array = new byte[162];
			for (int i = regStart; i <= regStop; i++)
			{
				array[i - regStart] = Convert.ToByte(Convert.ToInt32(Top_dGridView.Rows[i / 16].Cells[i % 16].Value.ToString(), 16));
			}
			if (!debug)
			{
				b = I2C_Guarded_WriteArray(Convert.ToByte(value), Convert.ToByte(regStart), Convert.ToUInt16(regStop - regStart + 1), array);
			}
			if (b == 0)
			{
				I2C_oper_tSSLabel.Text = "Transmission OK";
			}
			else
			{
				MessageBox.Show(b.ToString());
			}
			IOSetSel_refresh();
			BXID_refresh();
			TDC_PULSE_refresh();
			GPO_OUT_SEL_refresh();
			DEF_CONFIG_refresh();
			AFE_PULSE_refresh();
			CAP_MEAS_refresh();
			TOP_COMMANDS_refresh();
		}
	}

	private void Top_Quad_Sel_Change(object sender, EventArgs e)
	{
		if (sender.Equals(TopSelQuad_SW_chkBox))
		{
			SW_i2c_chkBox.Checked = TopSelQuad_SW_chkBox.Checked;
		}
		else if (sender.Equals(TopSelQuad_NW_chkBox))
		{
			NW_i2c_chkBox.Checked = TopSelQuad_NW_chkBox.Checked;
		}
		else if (sender.Equals(TopSelQuad_NE_chkBox))
		{
			NE_i2c_chkBox.Checked = TopSelQuad_NE_chkBox.Checked;
		}
		else if (sender.Equals(TopSelQuad_SE_chkBox))
		{
			SE_i2c_chkBox.Checked = TopSelQuad_SE_chkBox.Checked;
		}
	}

	private void DEF_CONFIG_refresh()
	{
		DEF_CONFIG_groupBox.Enabled = false;
		if (Convert.ToInt32(Top_dGridView.Rows[0].Cells[0].Value.ToString(), 16) == 220)
		{
			DEFAULT_CONFIG_chkBox.Checked = false;
		}
		else
		{
			DEFAULT_CONFIG_chkBox.Checked = true;
		}
		DEF_CONFIG_groupBox.Enabled = true;
	}

	private void DEF_CONFIG_Change(object sender, EventArgs e)
	{
		if (DEF_CONFIG_groupBox.Enabled && sender.Equals(DEFAULT_CONFIG_chkBox))
		{
			Top_dGridView.Rows[0].Cells[0].Value = (220 * Convert.ToInt32(DEFAULT_CONFIG_chkBox.Checked)).ToString("X2");
			Top_dGridView.CurrentCell = Top_dGridView[0, 0];
			TopI2C_write_single_but_Click(TopI2C_write_single_but, EventArgs.Empty);
		}
	}

	private void BXID_refresh()
	{
		BXID_groupBox.Enabled = false;
		int num = Convert.ToInt32(Top_dGridView.Rows[0].Cells[1].Value.ToString(), 16);
		int num2 = Convert.ToInt32(Top_dGridView.Rows[0].Cells[2].Value.ToString(), 16) & 0xF;
		BXID_PRL_UpDown.Value = 256 * num2 + num;
		num = Convert.ToInt32(Top_dGridView.Rows[0].Cells[3].Value.ToString(), 16);
		num2 = (Convert.ToInt32(Top_dGridView.Rows[0].Cells[2].Value.ToString(), 16) >> 4) & 0xF;
		BXID_LMT_UpDown.Value = 256 * num2 + num;
		BXID_groupBox.Enabled = true;
	}

	private void BXID_Change(object sender, EventArgs e)
	{
		if (BXID_groupBox.Enabled && (sender.Equals(BXID_PRL_UpDown) || sender.Equals(BXID_LMT_UpDown)))
		{
			Top_dGridView.Rows[0].Cells[1].Value = (Convert.ToInt32(BXID_PRL_UpDown.Value) & 0xFF).ToString("X2");
			int num = (Convert.ToInt32(BXID_PRL_UpDown.Value) >> 8) & 0xF;
			num += 16 * ((Convert.ToInt32(BXID_LMT_UpDown.Value) >> 8) & 0xF);
			Top_dGridView.Rows[0].Cells[2].Value = num.ToString("X2");
			Top_dGridView.Rows[0].Cells[3].Value = (Convert.ToInt32(BXID_LMT_UpDown.Value) & 0xFF).ToString("X2");
			TopI2C_write_range(1, 3);
		}
	}

	private void IOSetSel_refresh()
	{
		IOSetSel_groupBox.Enabled = false;
		int num = Convert.ToInt32(Top_dGridView.Rows[0].Cells[5].Value.ToString(), 16);
		int selectedIndex = num & 1;
		SLVS_TRM_comboBox.SelectedIndex = selectedIndex;
		selectedIndex = (num >> 1) & 1;
		SLVS_INVTX_chkBox.Checked = Convert.ToBoolean(selectedIndex);
		selectedIndex = (num >> 2) & 1;
		SLVS_INVRX_chkBox.Checked = Convert.ToBoolean(selectedIndex);
		selectedIndex = (num >> 3) & 1;
		FASTIN_EN_chkBox.Checked = Convert.ToBoolean(selectedIndex);
		selectedIndex = (num >> 7) & 1;
		FE_POLARITY_comboBox.SelectedIndex = selectedIndex;
		num = Convert.ToInt32(Top_dGridView.Rows[0].Cells[4].Value.ToString(), 16);
		selectedIndex = num & 0xF;
		SLVS_CMM_MODE_UpDown.Value = selectedIndex;
		selectedIndex = (num >> 4) & 0xF;
		SLVS_DRV_STR_UpDown.Value = selectedIndex;
		IOSetSel_groupBox.Enabled = true;
	}

	private void IOSetSel_Change(object sender, EventArgs e)
	{
		if (IOSetSel_groupBox.Enabled)
		{
			if (sender.Equals(SLVS_INVTX_chkBox) || sender.Equals(SLVS_INVRX_chkBox) || sender.Equals(SLVS_TRM_comboBox) || sender.Equals(FASTIN_EN_chkBox) || sender.Equals(FE_POLARITY_comboBox))
			{
				Top_dGridView.Rows[0].Cells[5].Value = (128 * FE_POLARITY_comboBox.SelectedIndex + 8 * Convert.ToInt32(FASTIN_EN_chkBox.Checked) + 4 * Convert.ToInt32(SLVS_INVRX_chkBox.Checked) + 2 * Convert.ToInt32(SLVS_INVTX_chkBox.Checked) + SLVS_TRM_comboBox.SelectedIndex).ToString("X2");
				Top_dGridView.CurrentCell = Top_dGridView[5, 0];
				TopI2C_write_single_but_Click(TopI2C_write_single_but, EventArgs.Empty);
			}
			else if (sender.Equals(SLVS_CMM_MODE_UpDown) || sender.Equals(SLVS_DRV_STR_UpDown))
			{
				Top_dGridView.Rows[0].Cells[4].Value = Convert.ToInt32(16m * SLVS_DRV_STR_UpDown.Value + SLVS_CMM_MODE_UpDown.Value).ToString("X2");
				Top_dGridView.CurrentCell = Top_dGridView[4, 0];
				TopI2C_write_single_but_Click(TopI2C_write_single_but, EventArgs.Empty);
			}
		}
	}

	private void TDC_PULSE_refresh()
	{
		TDC_PULSE_groupBox.Enabled = false;
		POINT_TA_UpDown.Value = Convert.ToInt32(Top_dGridView.Rows[0].Cells[6].Value.ToString(), 16) & 0xF;
		POINT_TOT_UpDown.Value = Convert.ToInt32(Top_dGridView.Rows[0].Cells[7].Value.ToString(), 16) & 0x1F;
		SEL_PULSE_SRC_comboBox.SelectedIndex = (Convert.ToInt32(Top_dGridView.Rows[0].Cells[6].Value.ToString(), 16) >> 4) & 3;
		TDC_PULSE_groupBox.Enabled = true;
	}

	private void TDC_PULSE_Change(object sender, EventArgs e)
	{
		if (TDC_PULSE_groupBox.Enabled)
		{
			if (sender.Equals(POINT_TA_UpDown) || sender.Equals(SEL_PULSE_SRC_comboBox))
			{
				Top_dGridView.Rows[0].Cells[6].Value = (16 * SEL_PULSE_SRC_comboBox.SelectedIndex + Convert.ToInt32(POINT_TA_UpDown.Value)).ToString("X2");
				Top_dGridView.CurrentCell = Top_dGridView[6, 0];
				TopI2C_write_single_but_Click(TopI2C_write_single_but, EventArgs.Empty);
			}
			else if (sender.Equals(POINT_TOT_UpDown))
			{
				Top_dGridView.Rows[0].Cells[7].Value = Convert.ToInt32(POINT_TOT_UpDown.Value).ToString("X2");
				Top_dGridView.CurrentCell = Top_dGridView[7, 0];
				TopI2C_write_single_but_Click(TopI2C_write_single_but, EventArgs.Empty);
			}
		}
	}

	private void TDC_PULSE_contMenuStripClick(object sender, EventArgs e)
	{
		foreach (Control control3 in Top_tabPage.Controls)
		{
			control3.Enabled = false;
		}
		int num = 0;
		if (sender.Equals(TOP_TDCpulse_x32times))
		{
			num = 32;
		}
		else if (sender.Equals(TOP_TDCpulse_x50times))
		{
			num = 50;
		}
		else if (sender.Equals(TOP_TDCpulse_x64times))
		{
			num = 64;
		}
		else if (sender.Equals(TOP_TDCpulse_x100times))
		{
			num = 100;
		}
		else if (sender.Equals(TOP_TDCpulse_x128times))
		{
			num = 128;
		}
		int value = Convert.ToInt32(TOP_I2C_addr_tBox.Text, 16);
		int value2 = 32;
		byte b = 0;
		int num2 = 64 * Convert.ToInt32(TOP_COMM_FRC_RST_CAL_chkBox.Checked) + 32 * Convert.ToInt32(TOP_COMM_START_AUTO_chkBox.Checked) + 16 * Convert.ToInt32(TOP_COMM_START_CAL_chkBox.Checked) + 2;
		if (debug)
		{
			MessageBox.Show($"Ripetizioni = {num}");
		}
		for (int i = 0; i < num; i++)
		{
			if (!debug)
			{
				b = I2C_Guarded_WriteByte(Convert.ToByte(value), Convert.ToByte(value2), Convert.ToByte(num2));
			}
			if (b == 0)
			{
				I2C_oper_tSSLabel.Text = "";
			}
			else
			{
				MessageBox.Show(b.ToString());
			}
			Task.Delay(1);
		}
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		Top_dGridView.Rows[2].Cells[0].Value = (num2 & 0x70).ToString("X2");
		foreach (Control control4 in Top_tabPage.Controls)
		{
			control4.Enabled = true;
		}
	}

	private void AFE_PULSE_refresh()
	{
		AFE_PULSE_groupBox.Enabled = false;
		AFE_UPDATE_TIME_UpDown.Value = Convert.ToInt32(Top_dGridView.Rows[0].Cells[8].Value.ToString(), 16);
		AFE_LISTEN_TIME_UpDown.Value = Convert.ToInt32(Top_dGridView.Rows[0].Cells[9].Value.ToString(), 16) & 0x3F;
		AFE_EN_TP_PHASE_UpDown.Value = (Convert.ToInt32(Top_dGridView.Rows[0].Cells[9].Value.ToString(), 16) >> 6) & 0x3F;
		AFE_TP_PERIOD_UpDown.Value = Convert.ToInt32(Top_dGridView.Rows[0].Cells[10].Value.ToString(), 16) & 0xF;
		AFE_TP_WIDTH_UpDown.Value = (Convert.ToInt32(Top_dGridView.Rows[0].Cells[10].Value.ToString(), 16) >> 4) & 7;
		AFE_TP_REPETITION_UpDown.Value = Convert.ToInt32(Top_dGridView.Rows[0].Cells[11].Value.ToString(), 16) & 0x3F;
		AFE_START_TP_chkBox.Checked = Convert.ToBoolean((Convert.ToInt32(Top_dGridView.Rows[0].Cells[11].Value.ToString(), 16) >> 6) & 1);
		AFE_EOS_chkBox.Checked = Convert.ToBoolean((Convert.ToInt32(Top_dGridView.Rows[0].Cells[11].Value.ToString(), 16) >> 7) & 1);
		AFE_PULSE_groupBox.Enabled = true;
	}

	private void AFE_PULSE_Change(object sender, EventArgs e)
	{
		if (AFE_PULSE_groupBox.Enabled)
		{
			if (sender.Equals(AFE_UPDATE_TIME_UpDown))
			{
				Top_dGridView.Rows[0].Cells[8].Value = Convert.ToInt32(AFE_UPDATE_TIME_UpDown.Value).ToString("X2");
				Top_dGridView.CurrentCell = Top_dGridView[8, 0];
				TopI2C_write_single_but_Click(TopI2C_write_single_but, EventArgs.Empty);
			}
			else if (sender.Equals(AFE_LISTEN_TIME_UpDown) || sender.Equals(AFE_EN_TP_PHASE_UpDown))
			{
				Top_dGridView.Rows[0].Cells[9].Value = (64 * Convert.ToInt32(AFE_EN_TP_PHASE_UpDown.Value) + Convert.ToInt32(AFE_LISTEN_TIME_UpDown.Value)).ToString("X2");
				Top_dGridView.CurrentCell = Top_dGridView[9, 0];
				TopI2C_write_single_but_Click(TopI2C_write_single_but, EventArgs.Empty);
			}
			else if (sender.Equals(AFE_TP_PERIOD_UpDown) || sender.Equals(AFE_TP_WIDTH_UpDown))
			{
				Top_dGridView.Rows[0].Cells[10].Value = (16 * Convert.ToInt32(AFE_TP_WIDTH_UpDown.Value) + Convert.ToInt32(AFE_TP_PERIOD_UpDown.Value)).ToString("X2");
				Top_dGridView.CurrentCell = Top_dGridView[10, 0];
				TopI2C_write_single_but_Click(TopI2C_write_single_but, EventArgs.Empty);
			}
			else if (sender.Equals(AFE_TP_REPETITION_UpDown) || sender.Equals(AFE_START_TP_chkBox))
			{
				int num = Convert.ToInt32(Top_dGridView.Rows[0].Cells[11].Value.ToString(), 16);
				Top_dGridView.Rows[0].Cells[11].Value = (128 * (num >> 7) + 64 * Convert.ToInt32(AFE_START_TP_chkBox.Checked) + Convert.ToInt32(AFE_TP_REPETITION_UpDown.Value)).ToString("X2");
				Top_dGridView.CurrentCell = Top_dGridView[11, 0];
				TopI2C_write_single_but_Click(TopI2C_write_single_but, EventArgs.Empty);
			}
		}
	}

	private void GPO_OUT_SEL_refresh()
	{
		GPO_OUT_SEL_groupBox.Enabled = false;
		GPO_SLVS_SEL_comboBox.SelectedIndex = (Convert.ToInt32(Top_dGridView.Rows[0].Cells[12].Value.ToString(), 16) >> 4) & 0xF;
		GPO_CMOS_SEL_comboBox.SelectedIndex = Convert.ToInt32(Top_dGridView.Rows[0].Cells[12].Value.ToString(), 16) & 0xF;
		SEL_RO_comboBox.SelectedIndex = (Convert.ToInt32(Top_dGridView.Rows[0].Cells[13].Value.ToString(), 16) >> 4) & 3;
		SER_CK_SEL_comboBox.SelectedIndex = Convert.ToInt32(Top_dGridView.Rows[0].Cells[13].Value.ToString(), 16) & 3;
		FIXED_PATTERN_UpDown.Value = Convert.ToInt32(Top_dGridView.Rows[0].Cells[15].Value.ToString(), 16);
		FIXED_PATTERN_textBox.Text = Top_dGridView.Rows[0].Cells[15].Value.ToString();
		GPO_OUT_SEL_groupBox.Enabled = true;
	}

	private void GPO_OUT_SEL_Change(object sender, EventArgs e)
	{
		if (GPO_OUT_SEL_groupBox.Enabled)
		{
			if (sender.Equals(GPO_SLVS_SEL_comboBox) || sender.Equals(GPO_CMOS_SEL_comboBox))
			{
				Top_dGridView.Rows[0].Cells[12].Value = (16 * GPO_SLVS_SEL_comboBox.SelectedIndex + GPO_CMOS_SEL_comboBox.SelectedIndex).ToString("X2");
				Top_dGridView.CurrentCell = Top_dGridView[12, 0];
				TopI2C_write_single_but_Click(TopI2C_write_single_but, EventArgs.Empty);
			}
			else if (sender.Equals(SEL_RO_comboBox) || sender.Equals(SER_CK_SEL_comboBox))
			{
				Top_dGridView.Rows[0].Cells[13].Value = (16 * SEL_RO_comboBox.SelectedIndex + SER_CK_SEL_comboBox.SelectedIndex).ToString("X2");
				Top_dGridView.CurrentCell = Top_dGridView[13, 0];
				TopI2C_write_single_but_Click(TopI2C_write_single_but, EventArgs.Empty);
			}
			else if (sender.Equals(FIXED_PATTERN_UpDown))
			{
				string value = Convert.ToInt32(FIXED_PATTERN_UpDown.Value).ToString("X2");
				FIXED_PATTERN_textBox.Text = value;
				Top_dGridView.Rows[0].Cells[15].Value = value;
				Top_dGridView.CurrentCell = Top_dGridView[15, 0];
				TopI2C_write_single_but_Click(TopI2C_write_single_but, EventArgs.Empty);
			}
		}
	}

	private void CAP_MEAS_refresh()
	{
		CAP_MEAS_groupBox.Enabled = false;
		int num = Convert.ToInt32(Top_dGridView.Rows[1].Cells[0].Value.ToString(), 16);
		int num2 = num & 7;
		CMES_CQ_20F_UpDown.Value = num2;
		num2 = (num >> 3) & 7;
		CMES_CF_20F_UpDown.Value = num2;
		num2 = (num >> 6) & 1;
		CMES_ARST_chkBox.Checked = Convert.ToBoolean(num2);
		num2 = (num >> 7) & 1;
		CMES_AEN_chkBox.Checked = Convert.ToBoolean(num2);
		CMES_CC_04F_UpDown.Value = Convert.ToInt32(Top_dGridView.Rows[1].Cells[1].Value.ToString(), 16) & 0xF;
		num = Convert.ToInt32(Top_dGridView.Rows[1].Cells[2].Value.ToString(), 16);
		num2 = num & 3;
		CMES_SEL_WAIT_UpDown.Value = num2;
		num2 = (num >> 4) & 1;
		CMES_DPOL_comboBox.SelectedIndex = num2;
		num2 = (num >> 5) & 1;
		CMES_QPOL_comboBox.SelectedIndex = num2;
		num2 = (num >> 6) & 1;
		CMES_DEN_chkBox.Checked = Convert.ToBoolean(num2);
		CAP_MEAS_groupBox.Enabled = true;
	}

	private void CAP_MEAS_Change(object sender, EventArgs e)
	{
		if (CAP_MEAS_groupBox.Enabled)
		{
			if (sender.Equals(CMES_AEN_chkBox) || sender.Equals(CMES_ARST_chkBox) || sender.Equals(CMES_CF_20F_UpDown) || sender.Equals(CMES_CQ_20F_UpDown))
			{
				Top_dGridView.Rows[1].Cells[0].Value = (128 * Convert.ToInt32(CMES_AEN_chkBox.Checked) + 64 * Convert.ToInt32(CMES_ARST_chkBox.Checked) + 8 * Convert.ToInt32(CMES_CF_20F_UpDown.Value) + Convert.ToInt32(CMES_CQ_20F_UpDown.Value)).ToString("X2");
				Top_dGridView.CurrentCell = Top_dGridView[0, 1];
				TopI2C_write_single_but_Click(TopI2C_write_single_but, EventArgs.Empty);
			}
			else if (sender.Equals(CMES_CC_04F_UpDown))
			{
				Top_dGridView.Rows[1].Cells[1].Value = Convert.ToInt32(CMES_CC_04F_UpDown.Value).ToString("X2");
				Top_dGridView.CurrentCell = Top_dGridView[1, 1];
				TopI2C_write_single_but_Click(TopI2C_write_single_but, EventArgs.Empty);
			}
			else if (sender.Equals(CMES_DEN_chkBox) || sender.Equals(CMES_SEL_WAIT_UpDown) || sender.Equals(CMES_QPOL_comboBox) || sender.Equals(CMES_DPOL_comboBox))
			{
				Top_dGridView.Rows[1].Cells[2].Value = (64 * Convert.ToInt32(CMES_DEN_chkBox.Checked) + 32 * CMES_QPOL_comboBox.SelectedIndex + 16 * CMES_DPOL_comboBox.SelectedIndex + Convert.ToInt32(CMES_SEL_WAIT_UpDown.Value)).ToString("X2");
				Top_dGridView.CurrentCell = Top_dGridView[2, 1];
				TopI2C_write_single_but_Click(TopI2C_write_single_but, EventArgs.Empty);
			}
		}
	}

	private void TOP_COMMANDS_refresh()
	{
		TOP_COMMANDS_groupBox.Enabled = false;
		TOP_COMM_START_CAL_chkBox.Checked = Convert.ToBoolean((Convert.ToInt32(Top_dGridView.Rows[2].Cells[0].Value.ToString(), 16) >> 4) & 1);
		TOP_COMM_START_AUTO_chkBox.Checked = Convert.ToBoolean((Convert.ToInt32(Top_dGridView.Rows[2].Cells[0].Value.ToString(), 16) >> 5) & 1);
		TOP_COMM_FRC_RST_CAL_chkBox.Checked = Convert.ToBoolean((Convert.ToInt32(Top_dGridView.Rows[2].Cells[0].Value.ToString(), 16) >> 6) & 1);
		TOP_COMMANDS_groupBox.Enabled = true;
	}

	private void TOP_COMMANDS_but_Click(object sender, EventArgs e)
	{
		I2C_oper_tSSLabel.Text = "Writing...";
		int value = Convert.ToInt32(TOP_I2C_addr_tBox.Text, 16);
		int value2 = 32;
		int num = 0;
		if (sender.Equals(TOP_DAQreset_but))
		{
			num = 1;
		}
		else if (sender.Equals(TOP_TDCpulse_but))
		{
			num = 2;
		}
		else if (sender.Equals(TOP_COMM_FRC_RST_CAL_chkBox) || sender.Equals(TOP_COMM_START_AUTO_chkBox) || sender.Equals(TOP_COMM_START_CAL_chkBox))
		{
			num = 0;
		}
		byte b = 0;
		int num2 = 64 * Convert.ToInt32(TOP_COMM_FRC_RST_CAL_chkBox.Checked) + 32 * Convert.ToInt32(TOP_COMM_START_AUTO_chkBox.Checked) + 16 * Convert.ToInt32(TOP_COMM_START_CAL_chkBox.Checked) + num;
		Top_dGridView.Rows[2].Cells[0].Value = (num2 & 0x70).ToString("X2");
		if (!debug)
		{
			b = I2C_Guarded_WriteByte(Convert.ToByte(value), Convert.ToByte(value2), Convert.ToByte(num2));
		}
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
		Task.Delay(50);
	}

	private void OpenDaqFifoForm()
	{
		DaqFifoForm child = new DaqFifoForm(this);
		lock (_openDaqFormsLock)
		{
			_openDaqForms.Add(child);
		}
		child.FormClosed += delegate
		{
			lock (_openDaqFormsLock)
			{
				_openDaqForms.Remove(child);
			}
		};
		child.Show();
	}

	public void DaqFifo_STOP_DAQ()
	{
		List<DaqFifoForm> list;
		lock (_openDaqFormsLock)
		{
			list = _openDaqForms.ToList();
		}
		foreach (DaqFifoForm item in list)
		{
			try
			{
				item.CancelStopToken();
			}
			catch (Exception ex)
			{
				Debug.WriteLine("Cancel failed for " + item.Name + ": " + ex.Message);
			}
		}
	}

	private void DaqFifoForm_but_click(object sender, EventArgs e)
	{
		OpenDaqFifoForm();
	}

	private void CALForm_but_click(object sender, EventArgs e)
	{
		Form form = new VisualizeCalibrationForm(this);
		form.Show();
	}

	private void MatAddr_comboBox_SelectedIndexChanged(object sender, EventArgs e)
	{
		if (MatAddr_comboBox.SelectedIndex >= 0 && MatAddr_comboBox.SelectedIndex <= 15)
		{
			MAT_I2C_addr_tBox.Text = (MatAddr_comboBox.SelectedIndex * 2).ToString("X2");
			MAT_I2C_addr_tBox.Enabled = false;
		}
		else if (MatAddr_comboBox.SelectedIndex == 16)
		{
			MAT_I2C_addr_tBox.Text = "FE";
			MAT_I2C_addr_tBox.Enabled = false;
		}
		else if (MatAddr_comboBox.SelectedIndex == 17)
		{
			MAT_I2C_addr_tBox.Text = "00";
			MAT_I2C_addr_tBox.Enabled = true;
		}
		else
		{
			MAT_I2C_addr_tBox.Enabled = false;
			MessageBox.Show("MatAddrSelectedIndex out of Range");
		}
		if (MatAddr_comboBox.SelectedIndex > 3 && MatAddr_comboBox.SelectedIndex < 8)
		{
			foreach (Control control3 in Mat_tabPage.Controls)
			{
				control3.Enabled = false;
			}
			MatAddr_comboBox.Enabled = true;
			return;
		}
		foreach (Control control4 in Mat_tabPage.Controls)
		{
			control4.Enabled = true;
		}
		MatAddr_comboBox.Enabled = true;
		MAT_I2C_addr_tBox.Enabled = false;
		if (MatAddr_comboBox.SelectedIndex == 17)
		{
			MAT_I2C_addr_tBox.Text = "00";
			MAT_I2C_addr_tBox.Enabled = true;
		}
	}

	private void MatI2C_write_single_but_Click(object sender, EventArgs e)
	{
		I2C_oper_tSSLabel.Text = "Writing...";
		int value = Convert.ToInt32(MAT_I2C_addr_tBox.Text, 16);
		int value2 = 16 * Mat_dGridView.CurrentRow.Index + Mat_dGridView.CurrentCell.ColumnIndex;
		byte b = 0;
		int value3 = Convert.ToInt32(Mat_dGridView.SelectedCells[0].Value.ToString(), 16);
		if (!debug)
		{
			b = I2C_Guarded_WriteByte(Convert.ToByte(value), Convert.ToByte(value2), Convert.ToByte(value3));
		}
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
		Task.Delay(1000);
		PIXset_refresh();
		MAT_DACset_refresh();
		MAT_AFE_refresh();
		config_refresh();
	}

	private void MatI2C_read_single_but_Click(object sender, EventArgs e)
	{
		I2C_oper_tSSLabel.Text = "Reading...";
		int value = Convert.ToInt32(MAT_I2C_addr_tBox.Text, 16);
		int value2 = 16 * Mat_dGridView.CurrentRow.Index + Mat_dGridView.CurrentCell.ColumnIndex;
		byte ReadData;
		byte b;
		if (debug)
		{
			Random random = new Random();
			ReadData = Convert.ToByte(random.Next(0, 255));
			b = 0;
		}
		else
		{
			b = I2C_Guarded_ReadByte(Convert.ToByte(value), Convert.ToByte(value2), out ReadData);
		}
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
			Mat_dGridView.SelectedCells[0].Value = ReadData.ToString("X2");
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
		Task.Delay(1000);
		PIXset_refresh();
		MAT_DACset_refresh();
		MAT_AFE_refresh();
		config_refresh();
	}

	private void MatI2C_write_all_but_Click(object sender, EventArgs e)
	{
		I2C_oper_tSSLabel.Text = "Writing...";
		int value = Convert.ToInt32(MAT_I2C_addr_tBox.Text, 16);
		byte b = 0;
		int num = 0;
		byte[] array = new byte[256];
		for (int i = 0; i <= 6; i++)
		{
			for (int j = 0; j < 16; j++)
			{
				if (16 * i + j <= 107)
				{
					array[num] = Convert.ToByte(Convert.ToInt32(Mat_dGridView.Rows[i].Cells[j].Value.ToString(), 16));
					num++;
				}
			}
		}
		if (!debug)
		{
			b = I2C_Guarded_WriteArray(Convert.ToByte(value), Convert.ToByte(0), 107, array);
		}
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmition OK";
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
		Task.Delay(100);
		config_refresh();
		PIXset_refresh();
		MAT_DACset_refresh();
		MAT_AFE_refresh();
	}

	private void MatI2C_read_all_but_Click(object sender, EventArgs e)
	{
		I2C_oper_tSSLabel.Text = "Reading...";
		int value = Convert.ToInt32(MAT_I2C_addr_tBox.Text, 16);
		byte b = 0;
		byte[] array = new byte[256];
		array.Initialize();
		if (!debug)
		{
			b = I2C_Guarded_ReadArray(Convert.ToByte(value), Convert.ToByte(0), 112, array);
		}
		else
		{
			Random random = new Random();
			for (int i = 0; i <= 107; i++)
			{
				array[i] = Convert.ToByte(random.Next(0, 255));
			}
			array[112] = Convert.ToByte(random.Next(0, 255));
		}
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
			for (int j = 0; j <= 8; j++)
			{
				for (int k = 0; k < 16; k++)
				{
					if (16 * j + k <= 107 || 16 * j + k == 112)
					{
						Mat_dGridView.Rows[j].Cells[k].Value = array[16 * j + k].ToString("X2");
					}
				}
			}
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
		Task.Delay(100);
		config_refresh();
		PIXset_refresh();
		MAT_DACset_refresh();
		MAT_AFE_refresh();
	}

	private void MatI2C_write_range(int regStart, int regStop)
	{
		if (regStart <= 108 && regStop <= 108)
		{
			I2C_oper_tSSLabel.Text = "Writing...";
			int value = Convert.ToInt32(MAT_I2C_addr_tBox.Text, 16);
			byte b = 0;
			byte[] array = new byte[256];
			for (int i = regStart; i <= regStop; i++)
			{
				array[i - regStart] = Convert.ToByte(Convert.ToInt32(Mat_dGridView.Rows[i / 16].Cells[i % 16].Value.ToString(), 16));
			}
			if (!debug)
			{
				b = I2C_Guarded_WriteArray(Convert.ToByte(value), Convert.ToByte(regStart), Convert.ToUInt16(regStop - regStart + 1), array);
			}
			if (b == 0)
			{
				I2C_oper_tSSLabel.Text = "Transmission OK";
			}
			else
			{
				MessageBox.Show(b.ToString());
			}
			Task.Delay(1000);
			config_refresh();
			PIXset_refresh();
			MAT_DACset_refresh();
			MAT_AFE_refresh();
		}
	}

	private void MatI2C_read_range(int regStart, int regStop)
	{
		I2C_oper_tSSLabel.Text = "Reading...";
		int value = Convert.ToInt32(MAT_I2C_addr_tBox.Text, 16);
		byte b = 0;
		byte[] array = new byte[256];
		if (!debug)
		{
			b = I2C_Guarded_ReadArray(Convert.ToByte(value), Convert.ToByte(regStart), Convert.ToUInt16(regStop - regStart + 1), array);
		}
		else
		{
			Random random = new Random();
			for (int i = regStart; i <= regStop; i++)
			{
				array[i - regStart] = Convert.ToByte(random.Next(0, 255));
			}
		}
		if (b == 0 || debug)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
			for (int j = regStart; j <= regStop; j++)
			{
				if (j / 16 >= 8 && j % 16 != 6 && j % 16 != 7 && j % 16 < 14)
				{
					Mat_dGridView.Rows[j / 16].Cells[j % 16].Value = array[j - regStart].ToString("X2");
				}
			}
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
		Task.Delay(1000);
		config_refresh();
		PIXset_refresh();
		MAT_DACset_refresh();
		MAT_AFE_refresh();
	}

	private void Mat_Quad_Sel_Change(object sender, EventArgs e)
	{
		if (sender.Equals(MatSelQuad_SW_chkBox))
		{
			SW_i2c_chkBox.Checked = MatSelQuad_SW_chkBox.Checked;
		}
		else if (sender.Equals(MatSelQuad_NW_chkBox))
		{
			NW_i2c_chkBox.Checked = MatSelQuad_NW_chkBox.Checked;
		}
		else if (sender.Equals(MatSelQuad_NE_chkBox))
		{
			NE_i2c_chkBox.Checked = MatSelQuad_NE_chkBox.Checked;
		}
		else if (sender.Equals(MatSelQuad_SE_chkBox))
		{
			SE_i2c_chkBox.Checked = MatSelQuad_SE_chkBox.Checked;
		}
	}

	private void MAT_DCO_COMMAND_but_Click(object sender, EventArgs e)
	{
		if (sender.Equals(DCOcalib_but) && !CAL_MODE_chkBox.Checked)
		{
			MessageBox.Show("Calibration Mode is not Enabled");
			return;
		}
		MAT_COMMANDS_groupBox.Enabled = false;
		int wByte = 0;
		if (sender.Equals(DCOcalib_but))
		{
			wByte = 16 * CAL_SEL_DCO_comboBox.SelectedIndex + 8 * Convert.ToInt32(MAT_DCO_GROUP_4863_chkBox.Checked) + 4 * Convert.ToInt32(MAT_DCO_GROUP_3247_chkBox.Checked) + 2 * Convert.ToInt32(MAT_DCO_GROUP_1631_chkBox.Checked) + Convert.ToInt32(MAT_DCO_GROUP_0015_chkBox.Checked);
		}
		else if (sender.Equals(CAL_SEL_DCO_comboBox))
		{
			wByte = 16 * CAL_SEL_DCO_comboBox.SelectedIndex;
		}
		else if (sender.Equals(DAQreset_but))
		{
			wByte = 128 + 16 * CAL_SEL_DCO_comboBox.SelectedIndex;
		}
		Mat_dGridView.Rows[7].Cells[0].Value = (16 * CAL_SEL_DCO_comboBox.SelectedIndex).ToString("X2");
		MAT_DCO_COMMAND(wByte);
		config_refresh();
		MAT_COMMANDS_groupBox.Enabled = true;
	}

	private void MAT_DCO_COMMAND(int wByte)
	{
		I2C_oper_tSSLabel.Text = "Writing...";
		int value = Convert.ToInt32(MAT_I2C_addr_tBox.Text, 16);
		int value2 = 112;
		byte b = 0;
		if (!debug)
		{
			b = I2C_Guarded_WriteByte(Convert.ToByte(value), Convert.ToByte(value2), Convert.ToByte(wByte));
		}
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
		Task.Delay(50);
	}

	private async void DCOtest_but_click(object sender, EventArgs e)
	{
		foreach (Control control3 in Mat_tabPage.Controls)
		{
			control3.Enabled = false;
		}
		Visualize_DCOCalibration_but.Enabled = true;
		CAL_MODE_chkBox.Checked = true;
		int num = 0;
		int value = 32 + SER_CK_SEL_comboBox.SelectedIndex;
		if (!debug)
		{
			num = I2C_Guarded_WriteByte(Convert.ToByte(252), Convert.ToByte(13), Convert.ToByte(value));
		}
		if (num == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(num.ToString());
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		await DCOtest();
		stopwatch.Stop();
		CAL_MODE_chkBox.Checked = false;
		MessageBox.Show($"DCO Test completed in {Math.Round(stopwatch.Elapsed.TotalSeconds, 1)} s");
		foreach (Control control4 in Mat_tabPage.Controls)
		{
			control4.Enabled = true;
		}
		if (MatAddr_comboBox.SelectedIndex != 17)
		{
			MAT_I2C_addr_tBox.Enabled = false;
		}
	}

	private async void DCOcalibration_but_click(object sender, EventArgs e)
	{
		foreach (Control control3 in Mat_tabPage.Controls)
		{
			control3.Enabled = false;
		}
		Visualize_DCOCalibration_but.Enabled = true;
		CAL_MODE_chkBox.Checked = true;
		int num = 0;
		int value = 32 + SER_CK_SEL_comboBox.SelectedIndex;
		if (!debug)
		{
			num = I2C_Guarded_WriteByte(Convert.ToByte(252), Convert.ToByte(13), Convert.ToByte(value));
		}
		if (num == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(num.ToString());
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		await DCOcal();
		stopwatch.Stop();
		CAL_MODE_chkBox.Checked = false;
		MessageBox.Show($"DCO Calibration completed in {Math.Round(stopwatch.Elapsed.TotalSeconds, 1)} s");
		foreach (Control control4 in Mat_tabPage.Controls)
		{
			control4.Enabled = true;
		}
		if (MatAddr_comboBox.SelectedIndex != 17)
		{
			MAT_I2C_addr_tBox.Enabled = false;
		}
	}

	private void DCOcalib_save_but_click(object sender, EventArgs e)
	{
		LoadedData.SaveCalibrationConfigurationToFile();
	}

	private async void TDCtest_but_click(object sender, EventArgs e)
	{
		foreach (Control control3 in Mat_tabPage.Controls)
		{
			control3.Enabled = false;
		}
		Visualize_DCOCalibration_but.Enabled = true;
		CAL_MODE_chkBox.Checked = false;
		int num = 0;
		int value = 32 + SER_CK_SEL_comboBox.SelectedIndex;
		if (!debug)
		{
			num = I2C_Guarded_WriteByte(Convert.ToByte(252), Convert.ToByte(13), Convert.ToByte(value));
		}
		if (num == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(num.ToString());
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		await TDCtest();
		stopwatch.Stop();
		CAL_MODE_chkBox.Checked = false;
		MessageBox.Show($"TDC Test completed in {Math.Round(stopwatch.Elapsed.TotalSeconds, 1)} s");
		foreach (Control control4 in Mat_tabPage.Controls)
		{
			control4.Enabled = true;
		}
		if (MatAddr_comboBox.SelectedIndex != 17)
		{
			MAT_I2C_addr_tBox.Enabled = false;
		}
	}

	private async void ATPtest_but_click(object sender, EventArgs e)
	{
		foreach (Control control3 in Mat_tabPage.Controls)
		{
			control3.Enabled = false;
		}
		Visualize_DCOCalibration_but.Enabled = true;
		CAL_MODE_chkBox.Checked = false;
		int num = 0;
		int value = 32 + SER_CK_SEL_comboBox.SelectedIndex;
		if (!debug)
		{
			num = I2C_Guarded_WriteByte(Convert.ToByte(252), Convert.ToByte(13), Convert.ToByte(value));
		}
		if (num == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(num.ToString());
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		await ATPtest();
		stopwatch.Stop();
		CAL_MODE_chkBox.Checked = false;
		MessageBox.Show($"TDC Test completed in {Math.Round(stopwatch.Elapsed.TotalSeconds, 1)} s");
		foreach (Control control4 in Mat_tabPage.Controls)
		{
			control4.Enabled = true;
		}
		if (MatAddr_comboBox.SelectedIndex != 17)
		{
			MAT_I2C_addr_tBox.Enabled = false;
		}
	}

	private async Task DCOtest()
	{
		int iMaxQuad = 1;
		int Quadstart = Cur_Quad;
		int iScan = 1;
		int iMaxDCO = 1;
		bool ON = true;
		bool OFF = false;
		using MultiTestSelForm form = new MultiTestSelForm("TestDCO");
		DialogResult dialogResult = form.ShowDialog();
		if (dialogResult != DialogResult.OK)
		{
			return;
		}
		DaqFifo_STOP_DAQ();
		int Quadrant = form.Quadrant;
		int MATnum = form.Mattonella;
		int PIXmin = form.PIXmin;
		int PIXmax = form.PIXmax;
		bool AllPIX = form.PIXall;
		int testType = form.TestType;
		int Cycles = form.Cycles;
		int DCOSel = form.DCOSel;
		int Delay = form.Delay;
		int CalibrationTime = form.CalibrationTime;
		bool EnableDoubleEdge = form.DoubleEdge;
		int SinglePointAdj = form.SingleAdj;
		int SinglePointCtrl = form.SingleCtrl;
		int[] TextColumnWidths = new int[25]
		{
			16, 11, 8, 3, 3, 3, 5, 10, 3, 2,
			8, 5, 5, 7, 9, 9, 10, 10, 10, 10,
			10, 10, 10, 10, 10
		};
		saveFileDialog1.FileName = "DCOtest_Quad" + form.DCO_Test_Quad_comboBox.SelectedItem.ToString().Substring(0, 2) + "_" + form.DCO_Test_MAT_comboBox.SelectedItem.ToString().Substring(0, 6) + "_PIX-" + form.DCO_Test_PIX_min_numUpDown.Value + "-" + form.DCO_Test_PIX_MAX_numUpDown.Value + "DATA_FIFO_IGNITE64" + DateTime.Now.ToString("_yy.MM.dd.HH.mm.ss") + ".txt";
		if (saveFileDialog1.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		string filename = saveFileDialog1.FileName;
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
		}
		Log_textBox.AppendText("\r\n" + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Log_textBox.AppendText("Starting DCO Test\r\n\r\n");
		if (Quadrant == 4)
		{
			iMaxQuad = 4;
		}
		int iMAT;
		for (int i = 0; i < iMaxQuad; i++)
		{
			if (Quadrant > 4)
			{
				continue;
			}
			byte b = 0;
			int value = Convert.ToInt32(Math.Pow(2.0, Quadrant));
			if (Quadrant == 4)
			{
				value = Convert.ToInt32(Math.Pow(2.0, i));
			}
			Cur_Quad = Quadrant;
			if (Quadrant == 4)
			{
				Cur_Quad = i;
				string[] array = new string[4] { "SW", "NW", "SE", "NE" };
				Log_textBox.AppendText("\r\n\tStarted test on Quadrant " + array[i]);
			}
			if (!debug)
			{
				b = I2C_Guarded_SendByte(Convert.ToByte(224), Convert.ToByte(value));
			}
			if (b == 0)
			{
				I2C_oper_tSSLabel.Text = "Transmission OK";
			}
			else
			{
				MessageBox.Show(b.ToString());
			}
			int iMaxMAT;
			if (MATnum == 16)
			{
				iMAT = 0;
				iMaxMAT = 15;
			}
			else
			{
				iMAT = MATnum;
				iMaxMAT = MATnum;
			}
			for (; iMAT <= iMaxMAT; iMAT++)
			{
				if (iMAT > 3 && iMAT < 8)
				{
					continue;
				}
				MatAddr_comboBox.SelectedIndex = Math.Min(iMAT, 15);
				EventArgs empty = EventArgs.Empty;
				MatAddr_comboBox_SelectedIndexChanged(MatAddr_comboBox, empty);
				MatI2C_read_all_but_Click(MatI2C_read_all_but, empty);
				Log_textBox.AppendText($"\r\n\tStarted test on MAT {iMAT}");
				MainForm mainForm = this;
				int matID = iMAT;
				bool? calMode = true;
				int? calTime = CalibrationTime;
				mainForm.Ignite32_Mat_CAL_conf_noUI(matID, null, null, null, null, null, calMode, calTime);
				if (DCOSel == 1)
				{
					Ignite32_Mat_TDC_DCO0conf_noUI(iMAT, EnableDoubleEdge, ON);
				}
				if (AllPIX && MATnum == 17)
				{
					PIXmin = 0;
					PIXmax = 63;
				}
				else if (AllPIX && MATnum != 17)
				{
					PIXmin = PIXmax;
				}
				for (int iPIX = PIXmin; iPIX <= PIXmax; iPIX++)
				{
					bool write_once = true;
					bool keep_conf = false;
					if (testType == 1)
					{
						iScan = 64;
					}
					if (testType == 2)
					{
						keep_conf = true;
					}
					for (int itype = 0; itype < iScan; itype++)
					{
						int AdjCtrl = itype;
						if (testType == 0)
						{
							AdjCtrl = 16 * SinglePointAdj + SinglePointCtrl;
						}
						for (int iCycle = 0; iCycle < Cycles; iCycle++)
						{
							if ((DCOSel == 0 || DCOSel == 2) && AllPIX && MATnum != 17)
							{
								MainForm mainForm2 = this;
								int matID2 = iMAT;
								calMode = ON;
								mainForm2.Ignite32_Mat_allPIX_conf_noUI(matID2, null, calMode);
							}
							else if ((DCOSel == 0 || DCOSel == 2) && (!AllPIX || MATnum == 17))
							{
								MainForm mainForm3 = this;
								int matID3 = iMAT;
								int pixID = iPIX;
								calMode = ON;
								mainForm3.Ignite32_Mat_PIX_conf_noUI(matID3, pixID, null, calMode);
							}
							if (DCOSel == 2)
							{
								iMaxDCO = 2;
							}
							for (int iDCO = 0; iDCO < iMaxDCO; iDCO++)
							{
								if (DCOSel == 0 || iDCO == 0)
								{
									if (keep_conf)
									{
										Ignite32_Mat_TDC_DCO0conf_noUI(iMAT, EnableDoubleEdge, ON);
									}
									else
									{
										Ignite32_Mat_TDC_DCO0conf_noUI(iMAT, EnableDoubleEdge, ON, AdjCtrl / 16, AdjCtrl % 16);
									}
									Ignite32_Mat_Command_noUI(iMAT, 0, OFF, ON, ON, ON, ON);
									DataIndex loadedData = LoadedData;
									DataIndex dataIndex = loadedData;
									int n_read_from_last_save = loadedData.n_read_from_last_save;
									dataIndex.n_read_from_last_save = n_read_from_last_save + await ReadUntilEmpty((ushort)AdjCtrl, (ushort)Ignite32_DCO_conf_read(iMAT, 1, iPIX), new_cal: true);
								}
								if (DCOSel != 1 && iDCO != 1)
								{
									continue;
								}
								if (AllPIX && MATnum != 17)
								{
									if (keep_conf)
									{
										MainForm mainForm4 = this;
										int matID4 = iMAT;
										calMode = ON;
										mainForm4.Ignite32_Mat_allPIX_conf_noUI(matID4, null, calMode);
									}
									else
									{
										MainForm mainForm5 = this;
										int matID5 = iMAT;
										calMode = ON;
										calTime = AdjCtrl / 16;
										int? ctrl = AdjCtrl % 16;
										mainForm5.Ignite32_Mat_allPIX_conf_noUI(matID5, null, calMode, calTime, ctrl);
									}
									Ignite32_Mat_Command_noUI(iMAT, 1, OFF, ON, ON, ON, ON);
									DataIndex loadedData2 = LoadedData;
									DataIndex dataIndex = loadedData2;
									int n_read_from_last_save = loadedData2.n_read_from_last_save;
									dataIndex.n_read_from_last_save = n_read_from_last_save + await Task.Run(() => ReadUntilEmpty((ushort)Ignite32_DCO_conf_read(iMAT, 0, iPIX), (ushort)AdjCtrl, new_cal: true));
									if (keep_conf)
									{
										MainForm mainForm6 = this;
										int matID6 = iMAT;
										calMode = OFF;
										mainForm6.Ignite32_Mat_allPIX_conf_noUI(matID6, null, calMode);
									}
								}
								else
								{
									if (keep_conf)
									{
										MainForm mainForm7 = this;
										int matID7 = iMAT;
										int pixID2 = iPIX;
										calMode = ON;
										mainForm7.Ignite32_Mat_PIX_conf_noUI(matID7, pixID2, null, calMode);
									}
									else
									{
										MainForm mainForm8 = this;
										int matID8 = iMAT;
										int pixID3 = iPIX;
										calMode = ON;
										int? ctrl = AdjCtrl / 16;
										calTime = AdjCtrl % 16;
										mainForm8.Ignite32_Mat_PIX_conf_noUI(matID8, pixID3, null, calMode, ctrl, calTime);
									}
									Ignite32_Mat_Command_noUI(iMAT, 1, OFF, ON, ON, ON, ON);
									DataIndex loadedData3 = LoadedData;
									DataIndex dataIndex = loadedData3;
									int n_read_from_last_save = loadedData3.n_read_from_last_save;
									dataIndex.n_read_from_last_save = n_read_from_last_save + await Task.Run(() => ReadUntilEmpty((ushort)Ignite32_DCO_conf_read(iMAT, 0, iPIX), (ushort)AdjCtrl, new_cal: true));
									if (keep_conf)
									{
										MainForm mainForm9 = this;
										int matID9 = iMAT;
										int pixID4 = iPIX;
										calMode = OFF;
										mainForm9.Ignite32_Mat_PIX_conf_noUI(matID9, pixID4, null, calMode);
									}
								}
								int num = Math.Max(1, iMaxQuad) * Math.Max(iMaxMAT, 1) * Math.Max(1, PIXmax) * Math.Max(1, iScan) * Math.Max(1, Cycles) * Math.Max(1, iMaxDCO);
								int num2 = i * (iMaxMAT * PIXmax * iScan * Cycles * iMaxDCO) + Math.Min(iMAT, 15) * (PIXmax * iScan * Cycles * iMaxDCO) + iPIX * (iScan * Cycles * iMaxDCO) + itype * (Cycles * iMaxDCO) + iCycle * iMaxDCO + iDCO;
								int num3 = num2 * 100 / num;
								if (num3 % 10 == 0 && num3 != 0 && write_once)
								{
									Log_textBox.AppendText($"\r\n\t=== DCO Test is {num3}% done ===\n");
									write_once = false;
								}
							}
							if (Delay > 0)
							{
								Task.Delay(1000 * Delay).Wait();
							}
						}
						if (!keep_conf)
						{
							MainForm mainForm10 = this;
							calMode = OFF;
							mainForm10.Ignite32_Mat_allPIX_conf_noUI(250, null, calMode);
						}
					}
				}
				MainForm mainForm11 = this;
				int matID10 = iMAT;
				calMode = false;
				calTime = CalibrationTime;
				mainForm11.Ignite32_Mat_CAL_conf_noUI(matID10, null, null, null, null, null, calMode, calTime);
				Log_textBox.AppendText($"\r\n\tFinished test on MAT {iMAT}\r\n");
			}
		}
		Log_textBox.AppendText("\r\n\tPreparing to write data to file");
		await Task.Run(delegate
		{
			Write_LastNdata_ToFile(filename, LoadedData.n_read_from_last_save);
		});
		LoadedData.n_read_from_last_save = 0;
		byte b2 = 0;
		int value2 = Convert.ToInt32(Math.Pow(2.0, Quadstart));
		Cur_Quad = Quadstart;
		if (!debug)
		{
			b2 = I2C_Guarded_SendByte(Convert.ToByte(224), Convert.ToByte(value2));
		}
		if (b2 == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(b2.ToString());
		}
		Log_textBox.AppendText("\r\n\tDONE writing data to file");
		Log_textBox.AppendText("\r\n" + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Log_textBox.AppendText(" ==> DCO Test DONE!\r\n");
	}

	private async Task DCOcal()
	{
		DaqFifo_STOP_DAQ();
		int iMaxQuad = 1;
		int Quadstart = Cur_Quad;
		bool ON = true;
		bool OFF = false;
		using MultiTestSelForm form = new MultiTestSelForm("CalDCO");
		DialogResult dialogResult = form.ShowDialog();
		if (dialogResult != DialogResult.OK)
		{
			return;
		}
		int Quadrant = form.Quadrant;
		int MATnum = form.Mattonella;
		int PIXmin = form.PIXmin;
		int PIXmax = form.PIXmax;
		bool AllPIX = form.PIXall;
		int Resolution = form.Delay;
		int CalibrationTime = form.CalibrationTime;
		bool EnableDoubleEdge = form.DoubleEdge;
		int SinglePointAdj = form.SingleAdj;
		int SinglePointCtrl = form.SingleCtrl;
		bool cal47 = form.CrossCouples;
		int DCO0_AdjCtrl = 16 * SinglePointAdj + SinglePointCtrl;
		int[] TextColumnWidths = new int[25]
		{
			16, 11, 8, 3, 3, 3, 5, 10, 3, 2,
			8, 5, 5, 7, 9, 9, 10, 10, 10, 10,
			10, 10, 10, 10, 10
		};
		saveFileDialog1.FileName = "CalDCO_Quad" + form.DCO_Test_Quad_comboBox.SelectedItem.ToString().Substring(0, 2) + "_" + form.DCO_Test_MAT_comboBox.SelectedItem.ToString().Substring(0, 6) + "_PIX-" + form.DCO_Test_PIX_min_numUpDown.Value + "-" + form.DCO_Test_PIX_MAX_numUpDown.Value + "DATA_FIFO_IGNITE64" + DateTime.Now.ToString("_yy.MM.dd.HH.mm.ss") + ".txt";
		if (saveFileDialog1.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		string filename = saveFileDialog1.FileName;
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
		}
		Log_textBox.AppendText("\r\n" + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Log_textBox.AppendText("Starting DCO Calibration\r\n\r\n");
		if (Quadrant == 4)
		{
			iMaxQuad = 4;
		}
		int iMAT;
		for (int i = 0; i < iMaxQuad; i++)
		{
			if (Quadrant > 4)
			{
				continue;
			}
			byte b = 0;
			int value = Convert.ToInt32(Math.Pow(2.0, Quadrant));
			if (Quadrant == 4)
			{
				value = Convert.ToInt32(Math.Pow(2.0, i));
			}
			Cur_Quad = Quadrant;
			if (Quadrant == 4)
			{
				Cur_Quad = i;
				string[] array = new string[4] { "SW", "NW", "SE", "NE" };
				Log_textBox.AppendText("\r\n\tStarted test on Quadrant " + array[i]);
			}
			if (!debug)
			{
				b = I2C_Guarded_SendByte(Convert.ToByte(224), Convert.ToByte(value));
			}
			if (b == 0)
			{
				I2C_oper_tSSLabel.Text = "Transmission OK";
			}
			else
			{
				MessageBox.Show(b.ToString());
			}
			if (cal47)
			{
				await DCOcal47(PIXmin, PIXmax, Resolution - 3, CalibrationTime, EnableDoubleEdge, SinglePointAdj, SinglePointCtrl);
			}
			int iMaxMAT;
			if (MATnum == 16)
			{
				iMAT = 0;
				iMaxMAT = 15;
			}
			else
			{
				iMAT = MATnum;
				iMaxMAT = MATnum;
			}
			for (; iMAT <= iMaxMAT; iMAT++)
			{
				if (iMAT > 3 && iMAT < 8)
				{
					continue;
				}
				Log_textBox.AppendText($"\r\n\tStarted calibration on MAT {iMAT}");
				MainForm mainForm = this;
				int matID = iMAT;
				bool? calMode = true;
				int? calTime = CalibrationTime;
				mainForm.Ignite32_Mat_CAL_conf_noUI(matID, null, null, null, null, null, calMode, calTime);
				Ignite32_Mat_TDC_DCO0conf_noUI(iMAT, EnableDoubleEdge, ON, SinglePointAdj, SinglePointCtrl);
				if (AllPIX && MATnum == 17)
				{
					PIXmin = 0;
					PIXmax = 63;
				}
				int? ctrl;
				for (int iPIX = PIXmin; iPIX <= PIXmax; iPIX++)
				{
					MainForm mainForm2 = this;
					int matID2 = iMAT;
					int pixID = iPIX;
					calMode = ON;
					mainForm2.Ignite32_Mat_PIX_conf_noUI(matID2, pixID, null, calMode);
					Ignite32_Mat_Command_noUI(iMAT, 0, OFF, ON, ON, ON, ON);
					DataIndex loadedData = LoadedData;
					DataIndex dataIndex = loadedData;
					int n_read_from_last_save = loadedData.n_read_from_last_save;
					dataIndex.n_read_from_last_save = n_read_from_last_save + await ReadUntilEmpty((ushort)DCO0_AdjCtrl, (ushort)Ignite32_DCO_conf_read(iMAT, 1, iPIX));
					List<DataEntry> DCO0 = LoadedData.GetByCustomKey(new StructuredKey((ushort)iMAT, (ushort)iPIX, 1, 0));
					bool looped_once = false;
					int AdjCtrl = DCO0_AdjCtrl + Math.Min(5, 64 - DCO0_AdjCtrl);
					bool IsDone = false;
					double margin = 50000.0;
					for (; AdjCtrl < 64; AdjCtrl++)
					{
						if (IsDone)
						{
							AdjCtrl++;
							break;
						}
						if (!AllPIX || AllPIX)
						{
							Ignite32_Mat_PIX_conf_noUI(iMAT, iPIX, OFF, ON, AdjCtrl / 16, AdjCtrl % 16);
							Ignite32_Mat_Command_noUI(iMAT, 1, OFF, ON, ON, ON, ON);
							DataIndex loadedData2 = LoadedData;
							dataIndex = loadedData2;
							n_read_from_last_save = loadedData2.n_read_from_last_save;
							dataIndex.n_read_from_last_save = n_read_from_last_save + await Task.Run(() => ReadUntilEmpty((ushort)Ignite32_DCO_conf_read(iMAT, 0, iPIX), (ushort)AdjCtrl, new_cal: true));
						}
						List<DataEntry> byCustomKey = LoadedData.GetByCustomKey(new StructuredKey((ushort)iMAT, (ushort)iPIX, 1, (ushort)1));
						DataEntry dco0 = DCO0.OrderByDescending((DataEntry entry) => entry.Order).First();
						DataEntry dataEntry = byCustomKey.OrderByDescending((DataEntry entry) => entry.Order).First();
						if (dco0.MAT != dataEntry.MAT || dco0.PIX != dataEntry.PIX)
						{
							continue;
						}
						double num = (double)Resolution - (dco0.DCO0_T_picoS - dataEntry.DCO1_T_picoS).Value;
						if (num + 3.0 > 0.0 && num + 3.0 < margin)
						{
							margin = num;
							LoadedData.Resolution_Matrix[Cur_Quad, dco0.MAT, dco0.PIX] = (dco0.DCO0_T_picoS - dataEntry.DCO1_T_picoS).Value;
							LoadedData.DCO_ConfPairs_Matrix[Cur_Quad, dco0.MAT, dco0.PIX, 0] = Convert.ToInt32(dco0.AdjCtrl_DCO0);
							LoadedData.DCO_ConfPairs_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 1] = Convert.ToInt32(dataEntry.AdjCtrl_DCO1);
							LoadedData.CAL_Matrix[Cur_Quad, dco0.MAT, dco0.PIX, 0] = dco0.DCO0_T_picoS.Value;
							LoadedData.CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 1] = dataEntry.DCO1_T_picoS.Value;
						}
						if (num > 0.0 && num < 4.0)
						{
							LoadedData.Resolution_Matrix[Cur_Quad, dco0.MAT, dco0.PIX] = (dco0.DCO0_T_picoS - dataEntry.DCO1_T_picoS).Value;
							LoadedData.DCO_ConfPairs_Matrix[Cur_Quad, dco0.MAT, dco0.PIX, 0] = Convert.ToInt32(dco0.AdjCtrl_DCO0);
							LoadedData.DCO_ConfPairs_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 1] = Convert.ToInt32(dataEntry.AdjCtrl_DCO1);
							MainForm mainForm3 = this;
							int matID3 = iMAT;
							int pixID2 = iPIX;
							calMode = OFF;
							calTime = AdjCtrl / 16;
							ctrl = AdjCtrl % 16;
							mainForm3.Ignite32_Mat_PIX_conf_noUI(matID3, pixID2, null, calMode, calTime, ctrl);
							LoadedData.CAL_Matrix[Cur_Quad, dco0.MAT, dco0.PIX, 0] = dco0.DCO0_T_picoS.Value;
							LoadedData.CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 1] = dataEntry.DCO1_T_picoS.Value;
							IsDone = true;
						}
						else if (AdjCtrl == 63 && !looped_once)
						{
							AdjCtrl = 0;
							looped_once = true;
						}
						else if (AdjCtrl == DCO0_AdjCtrl + Math.Min(5, 64 - DCO0_AdjCtrl) && looped_once)
						{
							IsDone = true;
							looped_once = false;
							int AdjCtrl_reserve = LoadedData.DCO_ConfPairs_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 1];
							MainForm mainForm4 = this;
							int matID4 = iMAT;
							int pixID3 = iPIX;
							calMode = ON;
							ctrl = AdjCtrl_reserve / 16;
							calTime = AdjCtrl_reserve % 16;
							mainForm4.Ignite32_Mat_PIX_conf_noUI(matID4, pixID3, null, calMode, ctrl, calTime);
							Ignite32_Mat_Command_noUI(iMAT, 1, OFF, ON, ON, ON, ON);
							DataIndex loadedData3 = LoadedData;
							dataIndex = loadedData3;
							n_read_from_last_save = loadedData3.n_read_from_last_save;
							dataIndex.n_read_from_last_save = n_read_from_last_save + await Task.Run(() => ReadUntilEmpty((ushort)Ignite32_DCO_conf_read(iMAT, 0, iPIX), (ushort)AdjCtrl_reserve, new_cal: true));
							byCustomKey = LoadedData.GetByCustomKey(new StructuredKey((ushort)iMAT, (ushort)iPIX, 1, (ushort)1));
							dataEntry = byCustomKey.OrderByDescending((DataEntry entry) => entry.Order).First();
							LoadedData.Resolution_Matrix[Cur_Quad, dco0.MAT, dco0.PIX] = (dco0.DCO0_T_picoS - dataEntry.DCO1_T_picoS).Value;
							LoadedData.CAL_Matrix[Cur_Quad, dco0.MAT, dco0.PIX, 0] = dco0.DCO0_T_picoS.Value;
							LoadedData.CAL_Matrix[Cur_Quad, dataEntry.MAT, dataEntry.PIX, 1] = dataEntry.DCO1_T_picoS.Value;
							MainForm mainForm5 = this;
							int matID5 = iMAT;
							int pixID4 = iPIX;
							calMode = OFF;
							calTime = AdjCtrl_reserve / 16;
							ctrl = AdjCtrl_reserve % 16;
							mainForm5.Ignite32_Mat_PIX_conf_noUI(matID5, pixID4, null, calMode, calTime, ctrl);
						}
					}
				}
				MainForm mainForm6 = this;
				int matID6 = iMAT;
				calMode = false;
				ctrl = CalibrationTime;
				mainForm6.Ignite32_Mat_CAL_conf_noUI(matID6, null, null, null, null, null, calMode, ctrl);
				Log_textBox.AppendText($"\r\n\tFinished calibration on MAT {iMAT}\r\n");
			}
		}
		Log_textBox.AppendText("\r\n\tPreparing to write data to file");
		await Task.Run(delegate
		{
			Write_LastNdata_ToFile(filename, LoadedData.n_read_from_last_save);
		});
		LoadedData.n_read_from_last_save = 0;
		byte b2 = 0;
		int value2 = Convert.ToInt32(Math.Pow(2.0, Quadstart));
		Cur_Quad = Quadstart;
		if (!debug)
		{
			b2 = I2C_Guarded_SendByte(Convert.ToByte(224), Convert.ToByte(value2));
		}
		if (b2 == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(b2.ToString());
		}
		Log_textBox.AppendText("\r\n\tDONE writing data to file");
		Log_textBox.AppendText("\r\n" + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Log_textBox.AppendText(" ==> DCO Calibration DONE!");
	}

	private async Task TDCtest()
	{
		DaqFifo_STOP_DAQ();
		int iMaxQuad = 1;
		int Quadstart = Cur_Quad;
		bool ON = true;
		bool OFF = false;
		int Couples = 1;
		int N_phases = 16;
		int threshold = 500;
		using MultiTestSelForm form = new MultiTestSelForm("TestTDC");
		DialogResult dialogResult = form.ShowDialog();
		if (dialogResult != DialogResult.OK)
		{
			return;
		}
		int Quadrant = form.Quadrant;
		int MATnum = form.Mattonella;
		int PIX_1 = form.PIXmin;
		int PIX_2 = form.PIXmax;
		bool AllPIX = form.PIXall;
		int Cycles = form.Cycles;
		int CoupleSelection = form.DCO_Test_DCO_comboBox.SelectedIndex;
		int testType = form.DCO_Test_Type_comboBox.SelectedIndex;
		threshold = form.CalibrationTime * 2;
		bool RandTA = form.DoubleEdge;
		bool cross_couples = form.CrossCouples;
		int[] TextColumnWidths = new int[25]
		{
			16, 11, 8, 3, 3, 3, 5, 10, 3, 2,
			8, 5, 5, 7, 9, 9, 10, 10, 10, 10,
			10, 10, 10, 10, 10
		};
		saveFileDialog1.FileName = "TDCtest_Quad" + form.DCO_Test_Quad_comboBox.SelectedItem.ToString().Substring(0, 2) + "_" + form.DCO_Test_MAT_comboBox.SelectedItem.ToString().Substring(0, 6) + "_PIX-" + form.DCO_Test_PIX_min_numUpDown.Value + "-" + form.DCO_Test_PIX_MAX_numUpDown.Value + "DATA_FIFO_IGNITE64" + DateTime.Now.ToString("_yy.MM.dd.HH.mm.ss") + ".txt";
		if (testType != 2)
		{
			threshold = 1022;
		}
		if (saveFileDialog1.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		string savefile = "";
		string filename = saveFileDialog1.FileName;
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
		}
		Stopwatch timer = Stopwatch.StartNew();
		Log_textBox.AppendText("\r\n" + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Log_textBox.AppendText("Starting TDC Test\r\n\r\n");
		int total_cycles = 0;
		if (Quadrant == 4)
		{
			iMaxQuad = 4;
		}
		for (int i = 0; i < iMaxQuad; i++)
		{
			if (Quadrant > 4)
			{
				continue;
			}
			byte b = 0;
			int value = Convert.ToInt32(Math.Pow(2.0, Quadrant));
			if (Quadrant == 4)
			{
				value = Convert.ToInt32(Math.Pow(2.0, i));
			}
			Cur_Quad = Quadrant;
			if (Quadrant == 4)
			{
				Cur_Quad = i;
				string[] array = new string[4] { "SW", "NW", "SE", "NE" };
				Log_textBox.AppendText("\r\n\tStarted test on Quadrant " + array[i]);
			}
			if (!debug)
			{
				b = I2C_Guarded_SendByte(Convert.ToByte(224), Convert.ToByte(value));
			}
			if (b == 0)
			{
				I2C_oper_tSSLabel.Text = "Transmission OK";
			}
			else
			{
				MessageBox.Show(b.ToString());
			}
			DataIndex loadedData = LoadedData;
			DataIndex dataIndex = loadedData;
			int n_read_from_last_save = loadedData.n_read_from_last_save;
			dataIndex.n_read_from_last_save = n_read_from_last_save + await Task.Run(() => ReadUntilEmpty(0, 0));
			string text = savefile;
			savefile = text + await Task.Run(() => Write_LastNdata_ToString(LoadedData.n_read_from_last_save, 999, 999, Quadrant));
			LoadedData.n_read_from_last_save = 0;
			int N_measure_cumulative = 0;
			Ignite32_TOP_Write_Single(6, 1, 2, 4);
			if (testType == 2)
			{
				Ignite32_TOP_Write_Single(6, 3, 2, 4);
				Cycles = 5000;
				N_phases = 3;
			}
			int iMAT;
			int iMaxMAT;
			if (MATnum == 16)
			{
				iMAT = 0;
				iMaxMAT = 15;
			}
			else
			{
				iMAT = MATnum;
				iMaxMAT = MATnum;
			}
			for (; iMAT <= iMaxMAT; iMAT++)
			{
				if (iMAT > 3 && iMAT < 8)
				{
					continue;
				}
				Log_textBox.AppendText($"\r\n\tStarted test on MAT {iMAT}");
				if (AllPIX)
				{
					Couples = 32;
					if (cross_couples)
					{
						Couples = 64;
					}
					for (int num = 0; num < 64; num++)
					{
						if (LoadedData.CAL_Matrix[Cur_Quad, iMAT, num, 0] == 0.0 || LoadedData.CAL_Matrix[Cur_Quad, iMAT, num, 1] == 0.0)
						{
							MessageBox.Show($"At least one channel ({num}) is not calibrated in MAT ({iMAT})");
							return;
						}
					}
				}
				else if (LoadedData.CAL_Matrix[Cur_Quad, iMAT, PIX_1, 0] == 0.0 || LoadedData.CAL_Matrix[Cur_Quad, iMAT, PIX_1, 1] == 0.0 || LoadedData.CAL_Matrix[Cur_Quad, iMAT, PIX_2, 0] == 0.0 || LoadedData.CAL_Matrix[Cur_Quad, iMAT, PIX_2, 1] == 0.0)
				{
					MessageBox.Show($"Channel ({PIX_1}) and/or ({PIX_2}) are not calibrated in MAT ({iMAT})");
					return;
				}
				MainForm mainForm = this;
				int matID = iMAT;
				bool? calMode = false;
				mainForm.Ignite32_Mat_CAL_conf_noUI(matID, null, null, null, null, null, calMode);
				n_read_from_last_save = 0;
				int tested_couples = 0;
				int N_measure_per_couple;
				while (tested_couples < Couples)
				{
					MainForm mainForm2 = this;
					int matID2 = iMAT;
					calMode = ON;
					mainForm2.Ignite32_Mat_TDC_DCO0conf_noUI(matID2, null, calMode);
					bool write_once = true;
					if (AllPIX && (CoupleSelection == 1 || CoupleSelection == 0))
					{
						PIX_1 = n_read_from_last_save;
						PIX_2 = 32 + n_read_from_last_save;
					}
					if (AllPIX && CoupleSelection == 2)
					{
						if (!cross_couples && n_read_from_last_save % 8 == 0 && n_read_from_last_save % 56 != 0)
						{
							n_read_from_last_save += 8;
						}
						PIX_1 = n_read_from_last_save;
						PIX_2 = PIX_1 + 8;
					}
					if (AllPIX && CoupleSelection == 3)
					{
						if (!cross_couples && n_read_from_last_save % 2 == 1 && n_read_from_last_save < 63)
						{
							n_read_from_last_save++;
						}
						PIX_1 = n_read_from_last_save;
						PIX_2 = PIX_1 + 1;
					}
					DataIndex loadedData2 = LoadedData;
					dataIndex = loadedData2;
					int n_read_from_last_save2 = loadedData2.n_read_from_last_save;
					dataIndex.n_read_from_last_save = n_read_from_last_save2 + await Task.Run(() => ReadUntilEmpty(0, 0));
					Ignite32_MAT_Write_Single(iMAT, 65, 128 + PIX_1, 8, 0);
					Ignite32_MAT_Write_Single(iMAT, 66, 128 + PIX_2, 8, 0);
					Ignite32_Mat_PIX_conf_noUI(iMAT, PIX_1, PIXON: ON, FE_ON: OFF);
					Ignite32_Mat_PIX_conf_noUI(iMAT, PIX_2, PIXON: ON, FE_ON: OFF);
					N_measure_per_couple = 0;
					for (n_read_from_last_save2 = 1; n_read_from_last_save2 < N_phases; n_read_from_last_save2++)
					{
						if (testType == 2)
						{
							Ignite32_TOP_Write_Single(5, 1, 1, 3);
						}
						int cur_TA = n_read_from_last_save2;
						long step1Ms = timer.ElapsedMilliseconds;
						if (!RandTA)
						{
							Ignite32_TOP_Write_Single(6, cur_TA, 4, 0);
						}
						for (int cur_cycle = 0; cur_cycle < Cycles; cur_cycle++)
						{
							if (RandTA)
							{
								Random random = new Random();
								cur_TA = random.Next(1, 15);
								Ignite32_TOP_Write_Single(6, cur_TA, 4, 0);
							}
							Ignite32_TOP_Write_Single(32, 1, 1, 1);
							total_cycles++;
							if (total_cycles % 50 == 0 || cur_cycle == Cycles - 1 || (tested_couples == Couples - 1 && cur_cycle == Cycles - 1 && n_read_from_last_save2 == 15) || testType == 2)
							{
								if (testType == 2)
								{
									Ignite32_TOP_Write_Single(5, 0, 1, 3);
								}
								DataIndex loadedData3 = LoadedData;
								dataIndex = loadedData3;
								int n_read_from_last_save3 = loadedData3.n_read_from_last_save;
								dataIndex.n_read_from_last_save = n_read_from_last_save3 + await Task.Run(() => ReadUntilEmpty(0, 0, new_cal: false, cur_TA, 10, threshold + 1));
								N_measure_per_couple += LoadedData.n_read_from_last_save - N_measure_cumulative;
								N_measure_cumulative = LoadedData.n_read_from_last_save;
								if (testType == 2)
								{
									Ignite32_TOP_Write_Single(5, 1, 1, 3);
									await Task.Delay(3);
								}
							}
							long num2 = timer.ElapsedMilliseconds - step1Ms;
							if (num2 > 30000)
							{
								N_measure_per_couple = threshold;
							}
							int num3 = Math.Max(1, iMaxQuad) * Math.Max(1, iMaxMAT) * Math.Max(1, Couples) * 16 * Math.Max(1, Cycles);
							_ = i * (iMaxMAT * Couples * 16 * Cycles) + iMAT * (Couples * 16 * Cycles) + tested_couples * (16 * Cycles) + n_read_from_last_save2 * Cycles + cur_cycle;
							int num4 = total_cycles * 100 / num3;
							if (num4 % 10 == 0 && num4 != 0 && write_once)
							{
								Log_textBox.AppendText($"\r\n\t=== TDC Test is {num4}% done ===\r\n");
								write_once = false;
							}
							if (testType == 2 && N_measure_per_couple >= threshold)
							{
								break;
							}
						}
						if (testType == 2 && N_measure_per_couple >= threshold)
						{
							Ignite32_TOP_Write_Single(5, 0, 1, 3);
							break;
						}
					}
					MainForm mainForm3 = this;
					int matID3 = iMAT;
					int pixID = PIX_1;
					calMode = OFF;
					mainForm3.Ignite32_Mat_PIX_conf_noUI(matID3, pixID, null, calMode);
					MainForm mainForm4 = this;
					int matID4 = iMAT;
					int pixID2 = PIX_2;
					calMode = OFF;
					mainForm4.Ignite32_Mat_PIX_conf_noUI(matID4, pixID2, null, calMode);
					n_read_from_last_save++;
					tested_couples++;
					MainForm mainForm5 = this;
					int matID5 = iMAT;
					calMode = OFF;
					mainForm5.Ignite32_Mat_TDC_DCO0conf_noUI(matID5, null, calMode);
				}
				DataIndex loadedData4 = LoadedData;
				dataIndex = loadedData4;
				N_measure_per_couple = loadedData4.n_read_from_last_save;
				dataIndex.n_read_from_last_save = N_measure_per_couple + await Task.Run(() => ReadUntilEmpty(0, 0));
				text = savefile;
				savefile = text + await Task.Run(() => Write_LastNdata_ToString(LoadedData.n_read_from_last_save, 999, 999, Quadrant));
				LoadedData.n_read_from_last_save = 0;
				Log_textBox.AppendText($"\r\n\tFinished test on MAT {iMAT}\r\n");
			}
		}
		Log_textBox.AppendText("\r\n\tPreparing to write data to file");
		using (StreamWriter writer = new StreamWriter(filename, append: true))
		{
			await writer.WriteAsync(savefile);
			await writer.WriteLineAsync();
		}
		byte b2 = 0;
		int value2 = Convert.ToInt32(Math.Pow(2.0, Quadstart));
		Cur_Quad = Quadstart;
		if (!debug)
		{
			b2 = I2C_Guarded_SendByte(Convert.ToByte(224), Convert.ToByte(value2));
		}
		if (b2 == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(b2.ToString());
		}
		Log_textBox.AppendText("\r\n\tDONE writing data to file");
		Log_textBox.AppendText("\r\n" + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Log_textBox.AppendText(" ==> TDC Test DONE!\r\n");
	}

	private async Task ATPtest()
	{
		DaqFifo_STOP_DAQ();
		int iMaxQuad = 1;
		int Quadstart = Cur_Quad;
		bool ON = true;
		bool OFF = false;
		int Couples = 1;
		using MultiTestSelForm form = new MultiTestSelForm("TestATP");
		DialogResult dialogResult = form.ShowDialog();
		if (dialogResult != DialogResult.OK)
		{
			return;
		}
		int Quadrant = form.Quadrant;
		int MATnum = form.Mattonella;
		int PIX_1 = form.PIXmin;
		int PIX_2 = form.PIXmax;
		bool AllPIX = form.PIXall;
		int Cycles = form.Cycles;
		int CoupleSelection = form.DCO_Test_DCO_comboBox.SelectedIndex;
		bool cross_couples = form.CrossCouples;
		int[] TextColumnWidths = new int[25]
		{
			16, 11, 8, 3, 3, 3, 5, 10, 3, 2,
			8, 5, 5, 7, 9, 9, 10, 10, 10, 10,
			10, 10, 10, 10, 10
		};
		saveFileDialog1.FileName = "ATPtest_Quad" + form.DCO_Test_Quad_comboBox.SelectedItem.ToString().Substring(0, 2) + "_" + form.DCO_Test_MAT_comboBox.SelectedItem.ToString().Substring(0, 6) + "_PIX-" + form.DCO_Test_PIX_min_numUpDown.Value + "-" + form.DCO_Test_PIX_MAX_numUpDown.Value + "DATA_FIFO_IGNITE64" + DateTime.Now.ToString("_yy.MM.dd.HH.mm.ss") + ".txt";
		if (saveFileDialog1.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		string savefile = "";
		string filename = saveFileDialog1.FileName;
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
		}
		Log_textBox.AppendText("\r\n" + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Log_textBox.AppendText("Starting ATP Test\r\n\r\n");
		int total_cycles = 0;
		if (Quadrant == 4)
		{
			iMaxQuad = 4;
		}
		for (int i = 0; i < iMaxQuad; i++)
		{
			if (Quadrant > 4)
			{
				continue;
			}
			byte b = 0;
			int value = Convert.ToInt32(Math.Pow(2.0, Quadrant));
			if (Quadrant == 4)
			{
				value = Convert.ToInt32(Math.Pow(2.0, i));
			}
			Cur_Quad = Quadrant;
			if (Quadrant == 4)
			{
				Cur_Quad = i;
				string[] array = new string[4] { "SW", "NW", "SE", "NE" };
				Log_textBox.AppendText("\r\n\tStarted test on Quadrant " + array[i]);
			}
			if (!debug)
			{
				b = I2C_Guarded_SendByte(Convert.ToByte(224), Convert.ToByte(value));
			}
			if (b == 0)
			{
				I2C_oper_tSSLabel.Text = "Transmission OK";
			}
			else
			{
				MessageBox.Show(b.ToString());
			}
			Ignite32_TOP_Write_Single(11, 63, 6, 0);
			int iMAT;
			int iMaxMAT;
			if (MATnum == 16)
			{
				iMAT = 0;
				iMaxMAT = 15;
			}
			else
			{
				iMAT = MATnum;
				iMaxMAT = MATnum;
			}
			for (; iMAT <= iMaxMAT; iMAT++)
			{
				if (iMAT > 3 && iMAT < 8)
				{
					continue;
				}
				bool write_once = true;
				Log_textBox.AppendText($"\r\n\tStarted test on MAT {iMAT}");
				if (AllPIX)
				{
					Couples = 32;
					if (cross_couples)
					{
						Couples = 64;
					}
					for (int j = 0; j < 64; j++)
					{
						if (LoadedData.CAL_Matrix[Cur_Quad, iMAT, j, 0] == 0.0 || LoadedData.CAL_Matrix[Cur_Quad, iMAT, j, 1] == 0.0)
						{
							MessageBox.Show($"At least one channel ({j}) is not calibrated in MAT ({iMAT})");
							return;
						}
					}
				}
				else if (LoadedData.CAL_Matrix[Cur_Quad, iMAT, PIX_1, 0] == 0.0 || LoadedData.CAL_Matrix[Cur_Quad, iMAT, PIX_1, 1] == 0.0 || LoadedData.CAL_Matrix[Cur_Quad, iMAT, PIX_2, 0] == 0.0 || LoadedData.CAL_Matrix[Cur_Quad, iMAT, PIX_2, 1] == 0.0)
				{
					MessageBox.Show($"Channel ({PIX_1}) and/or ({PIX_2}) are not calibrated in MAT ({iMAT})");
					return;
				}
				MainForm mainForm = this;
				int matID = iMAT;
				bool? calMode = false;
				mainForm.Ignite32_Mat_CAL_conf_noUI(matID, null, null, null, null, null, calMode);
				MainForm mainForm2 = this;
				int matID2 = iMAT;
				calMode = ON;
				mainForm2.Ignite32_Mat_TDC_DCO0conf_noUI(matID2, null, calMode);
				int couple = 0;
				for (int tested_couples = 0; tested_couples < Couples; tested_couples++)
				{
					if (AllPIX && (CoupleSelection == 1 || CoupleSelection == 0))
					{
						PIX_1 = couple;
						PIX_2 = 32 + couple;
					}
					if (AllPIX && CoupleSelection == 2)
					{
						if (!cross_couples && couple % 8 == 0 && couple % 56 != 0)
						{
							couple += 8;
						}
						PIX_1 = couple;
						PIX_2 = PIX_1 + 8;
					}
					if (AllPIX && CoupleSelection == 3)
					{
						if (!cross_couples && couple % 2 == 1 && couple < 63)
						{
							couple++;
						}
						PIX_1 = couple;
						PIX_2 = PIX_1 + 1;
					}
					Ignite32_MAT_Write_Single(iMAT, 65, 192 + PIX_1, 8, 0);
					Ignite32_MAT_Write_Single(iMAT, 66, 192 + PIX_2, 8, 0);
					Ignite32_Mat_PIX_conf_noUI(iMAT, PIX_1, PIXON: ON, FE_ON: ON);
					Ignite32_Mat_PIX_conf_noUI(iMAT, PIX_2, PIXON: ON, FE_ON: ON);
					for (int cur_cycle = 0; cur_cycle < Cycles; cur_cycle++)
					{
						Ignite32_TOP_Write_Single(11, 1, 1, 6);
						await Task.Delay(1);
						Ignite32_TOP_Write_Single(11, 0, 1, 6);
						DataIndex loadedData = LoadedData;
						DataIndex dataIndex = loadedData;
						int n_read_from_last_save = loadedData.n_read_from_last_save;
						dataIndex.n_read_from_last_save = n_read_from_last_save + await Task.Run(() => ReadUntilEmpty(0, 0));
						total_cycles++;
						if (total_cycles % 60 == 0 || cur_cycle == Cycles - 1 || (tested_couples == Couples - 1 && cur_cycle == Cycles - 1))
						{
							string text = savefile;
							savefile = text + await Task.Run(() => Write_LastNdata_ToString(LoadedData.n_read_from_last_save, 999, 999, Quadrant));
							LoadedData.n_read_from_last_save = 0;
						}
						int num = Math.Max(1, iMaxQuad) * Math.Max(1, iMaxMAT) * Math.Max(1, Couples) * 16 * Math.Max(1, Cycles);
						_ = i * (iMaxMAT * Couples * 16 * Cycles) + iMAT * (Couples * 16 * Cycles) + tested_couples * (16 * Cycles) + Cycles + cur_cycle;
						int num2 = total_cycles * 100 / num;
						if (num2 % 10 == 0 && num2 != 0 && write_once)
						{
							Log_textBox.AppendText($"\r\n\t=== DCO Test is {num2}% done ===\r\n");
							write_once = false;
						}
					}
					Ignite32_Mat_PIX_conf_noUI(iMAT, PIX_1, PIXON: OFF, FE_ON: OFF);
					Ignite32_Mat_PIX_conf_noUI(iMAT, PIX_2, PIXON: OFF, FE_ON: OFF);
					couple++;
				}
				MainForm mainForm3 = this;
				int matID3 = iMAT;
				calMode = OFF;
				mainForm3.Ignite32_Mat_TDC_DCO0conf_noUI(matID3, null, calMode);
				Log_textBox.AppendText($"\r\n\tFinished test on MAT {iMAT}\r\n");
			}
		}
		Log_textBox.AppendText("\r\n\tPreparing to write data to file");
		using (StreamWriter writer = new StreamWriter(filename, append: true))
		{
			await writer.WriteAsync(savefile);
			await writer.WriteLineAsync();
		}
		byte b2 = 0;
		int value2 = Convert.ToInt32(Math.Pow(2.0, Quadstart));
		Cur_Quad = Quadstart;
		if (!debug)
		{
			b2 = I2C_Guarded_SendByte(Convert.ToByte(224), Convert.ToByte(value2));
		}
		if (b2 == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(b2.ToString());
		}
		Log_textBox.AppendText("\r\n\tDONE writing data to file");
		Log_textBox.AppendText("\r\n" + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Log_textBox.AppendText(" ==> ATP Test DONE!\r\n");
	}

	private async Task DCOcal47(int PIXmin = 0, int PIXmax = 63, int minLSB = 25, int CalTime = 3, bool DE = false, int DCO0adj = 1, int DCO0ctrl = 0)
	{
		int BROADCAST = 254;
		int pix_calibrated = 0;
		int cur_pix = PIXmin;
		int cur_pix_loop = 0;
		int cur_adjctr = 16 * DCO0adj + DCO0ctrl;
		double last_LSB = 5000.0;
		bool went_back = false;
		Ignite32_Mat_CAL_conf_noUI(BROADCAST, CalMode: true, CalTime: CalTime, EN_CON_PAD: false, EN_P_VTH: false, EN_P_Vldo: false, EN_P_VFB: false, EN_TimeOut: false);
		Ignite32_Mat_TDC_DCO0conf_noUI(BROADCAST, DE, true, DCO0adj, DCO0ctrl);
		while (pix_calibrated <= PIXmax - PIXmin && cur_pix_loop < 64)
		{
			Ignite32_Mat_PIX_conf_noUI(BROADCAST, cur_pix, false, true, cur_adjctr / 16, cur_adjctr % 16);
			Ignite32_Mat_Command_noUI(BROADCAST, 0, false, true, true, true, true);
			DataIndex loadedData = LoadedData;
			DataIndex dataIndex = loadedData;
			int n_read_from_last_save = loadedData.n_read_from_last_save;
			dataIndex.n_read_from_last_save = n_read_from_last_save + await ReadUntilEmpty((ushort)(16 * DCO0adj + DCO0ctrl), (ushort)cur_adjctr);
			Ignite32_Mat_Command_noUI(BROADCAST, 1, false, true, true, true, true);
			DataIndex loadedData2 = LoadedData;
			dataIndex = loadedData2;
			n_read_from_last_save = loadedData2.n_read_from_last_save;
			dataIndex.n_read_from_last_save = n_read_from_last_save + await ReadUntilEmpty((ushort)(16 * DCO0adj + DCO0ctrl), (ushort)cur_adjctr);
			List<DataEntry> list = new List<DataEntry>();
			List<DataEntry> list2 = new List<DataEntry>();
			for (int i = 4; i < 8; i++)
			{
				List<DataEntry> byCustomKey = LoadedData.GetByCustomKey(new StructuredKey((ushort)i, (ushort)cur_pix, 1, 0));
				List<DataEntry> byCustomKey2 = LoadedData.GetByCustomKey(new StructuredKey((ushort)i, (ushort)cur_pix, 1, (ushort)1));
				list.Add(byCustomKey.OrderByDescending((DataEntry entry) => entry.Order).First());
				list2.Add(byCustomKey2.OrderByDescending((DataEntry entry) => entry.Order).First());
			}
			for (int num = 0; num < 4; num++)
			{
				double val = (list[num].DCO0_T_picoS - list2[num].DCO1_T_picoS).Value;
				last_LSB = Math.Min(last_LSB, val);
			}
			if (last_LSB < (double)minLSB)
			{
				cur_adjctr++;
				last_LSB = 5000.0;
			}
			else if (last_LSB > (double)(minLSB + 4) && !went_back)
			{
				cur_adjctr--;
				went_back = true;
			}
			else
			{
				for (int num2 = 0; num2 < 4; num2++)
				{
					double num3 = (list[num2].DCO0_T_picoS - list2[num2].DCO1_T_picoS).Value;
					LoadedData.Resolution_Matrix[Cur_Quad, num2 + 4, cur_pix] = num3;
					LoadedData.DCO_ConfPairs_Matrix[Cur_Quad, num2 + 4, cur_pix, 0] = Convert.ToInt32(list[num2].AdjCtrl_DCO0);
					LoadedData.DCO_ConfPairs_Matrix[Cur_Quad, num2 + 4, cur_pix, 1] = Convert.ToInt32(list2[num2].AdjCtrl_DCO1);
					LoadedData.CAL_Matrix[Cur_Quad, num2 + 4, cur_pix, 0] = list[num2].DCO0_T_picoS.Value;
					LoadedData.CAL_Matrix[Cur_Quad, num2 + 4, cur_pix, 1] = list2[num2].DCO1_T_picoS.Value;
					Ignite32_Mat_PIX_conf_noUI(BROADCAST, cur_pix, false, false, cur_adjctr / 16, cur_adjctr % 16);
				}
				last_LSB = 5000.0;
				cur_adjctr = 16 * DCO0adj + DCO0ctrl;
				pix_calibrated++;
				cur_pix++;
				cur_pix_loop = -1;
			}
			cur_pix_loop++;
		}
	}

	private void config_refresh()
	{
		MAT_cfg_groupBox.Enabled = false;
		int num = Convert.ToInt32(Mat_dGridView.Rows[4].Cells[0].Value.ToString(), 16);
		enDEtot_chkBox.Checked = Convert.ToBoolean(num & 0x80);
		TDC_ON_chkBox.Checked = Convert.ToBoolean(num & 0x40);
		DCO0adj_UpDown.Value = (num >> 4) & 3;
		DCO0ctrl_UpDown.Value = num & 0xF;
		LoadedData.DCO_ConfPairs_Matrix[Cur_Quad, MatAddr_comboBox.SelectedIndex % 15, Convert.ToInt32(PIX_Sel_UpDown.Value), 0] = 16 * ((num >> 4) & 3) + (num & 0xF);
		num = Convert.ToInt32(Mat_dGridView.Rows[4].Cells[1].Value.ToString(), 16);
		CH_MODE_41_comboBox.SelectedIndex = (num >> 6) & 3;
		CH_SEL_41_UpDown.Value = num & 0x3F;
		num = Convert.ToInt32(Mat_dGridView.Rows[4].Cells[2].Value.ToString(), 16);
		CH_MODE_42_comboBox.SelectedIndex = (num >> 6) & 3;
		CH_SEL_42_UpDown.Value = num & 0x3F;
		num = Convert.ToInt32(Mat_dGridView.Rows[4].Cells[3].Value.ToString(), 16);
		EN_TIMEOUT_chkBox.Checked = Convert.ToBoolean(num & 8);
		CAL_MODE_chkBox.Checked = Convert.ToBoolean(num & 4);
		SEL_CAL_TIME_UpDown.Value = num & 3;
		CAL_SEL_DCO_comboBox.SelectedIndex = (Convert.ToInt32(Mat_dGridView.Rows[7].Cells[0].Value.ToString(), 16) >> 4) & 1;
		MAT_cfg_groupBox.Enabled = true;
	}

	private void config_mode_change(object sender, EventArgs e)
	{
		if (MAT_cfg_groupBox.Enabled)
		{
			if (sender.Equals(enDEtot_chkBox) || sender.Equals(TDC_ON_chkBox) || sender.Equals(DCO0adj_UpDown) || sender.Equals(DCO0ctrl_UpDown))
			{
				Mat_dGridView.Rows[4].Cells[0].Value = (Convert.ToInt32(enDEtot_chkBox.Checked) * 128 + Convert.ToInt32(TDC_ON_chkBox.Checked) * 64 + Convert.ToInt32(DCO0adj_UpDown.Value) * 16 + Convert.ToInt32(DCO0ctrl_UpDown.Value)).ToString("X2");
				Mat_dGridView.CurrentCell = Mat_dGridView[0, 4];
				MatI2C_write_single_but_Click(sender, e);
			}
			else if (sender.Equals(EN_TIMEOUT_chkBox) || sender.Equals(CAL_MODE_chkBox) || sender.Equals(SEL_CAL_TIME_UpDown))
			{
				int num = Convert.ToInt32(Mat_dGridView.Rows[4].Cells[3].Value.ToString(), 16);
				Mat_dGridView.Rows[4].Cells[3].Value = (128 * (num >> 7) + 64 * ((num >> 6) & 1) + 32 * ((num >> 5) & 1) + 16 * ((num >> 4) & 1) + 8 * Convert.ToInt32(EN_TIMEOUT_chkBox.Checked) + 4 * Convert.ToInt32(CAL_MODE_chkBox.Checked) + Convert.ToInt32(SEL_CAL_TIME_UpDown.Value)).ToString("X2");
				Mat_dGridView.CurrentCell = Mat_dGridView[3, 4];
				MatI2C_write_single_but_Click(sender, e);
			}
			else if (sender.Equals(CH_MODE_41_comboBox) || sender.Equals(CH_SEL_41_UpDown))
			{
				Mat_dGridView.Rows[4].Cells[1].Value = (64 * Convert.ToInt32(CH_MODE_41_comboBox.SelectedIndex) + Convert.ToInt32(CH_SEL_41_UpDown.Value)).ToString("X2");
				Mat_dGridView.CurrentCell = Mat_dGridView[1, 4];
				MatI2C_write_single_but_Click(sender, e);
			}
			else if (sender.Equals(CH_MODE_42_comboBox) || sender.Equals(CH_SEL_42_UpDown))
			{
				Mat_dGridView.Rows[4].Cells[2].Value = (64 * Convert.ToInt32(CH_MODE_42_comboBox.SelectedIndex) + Convert.ToInt32(CH_SEL_42_UpDown.Value)).ToString("X2");
				Mat_dGridView.CurrentCell = Mat_dGridView[2, 4];
				MatI2C_write_single_but_Click(sender, e);
			}
		}
	}

	private void PIXsetNum_change(object sender, EventArgs e)
	{
		Mat_dGridView.CurrentCell = Mat_dGridView[Convert.ToInt32(PIX_Sel_UpDown.Value) % 16, Convert.ToInt32(PIX_Sel_UpDown.Value) / 16];
		PIXset_refresh();
	}

	private void PIXset_refresh()
	{
		PIX_groupBox.Enabled = false;
		int num = Convert.ToInt32(Mat_dGridView.Rows[Convert.ToInt32(PIX_Sel_UpDown.Value) / 16].Cells[Convert.ToInt32(PIX_Sel_UpDown.Value) % 16].Value.ToString(), 16);
		DCO_PIX_ctrl_UpDown.Value = num & 0xF;
		DCO_PIX_adj_UpDown.Value = (num >> 4) & 3;
		PIX_ON_chkBox.Checked = Convert.ToBoolean(num & 0x40);
		MAT_FE_ON_chkBox.Checked = Convert.ToBoolean(num & 0x80);
		if (MatAddr_comboBox.SelectedIndex < 16)
		{
			MAT_DCO1_Period_textBox.Text = Math.Round(LoadedData.CAL_Matrix[Cur_Quad, MatAddr_comboBox.SelectedIndex, Convert.ToInt32(PIX_Sel_UpDown.Value), 1], 2).ToString();
			MAT_DCO0_Period_textBox.Text = Math.Round(LoadedData.CAL_Matrix[Cur_Quad, MatAddr_comboBox.SelectedIndex, Convert.ToInt32(PIX_Sel_UpDown.Value), 0], 2).ToString();
			MAT_DCO_Difference_textBox.Text = Math.Round(LoadedData.CAL_Matrix[Cur_Quad, MatAddr_comboBox.SelectedIndex, Convert.ToInt32(PIX_Sel_UpDown.Value), 0] - LoadedData.CAL_Matrix[Cur_Quad, MatAddr_comboBox.SelectedIndex, Convert.ToInt32(PIX_Sel_UpDown.Value), 1], 2).ToString();
			LoadedData.DCO_ConfPairs_Matrix[Cur_Quad, MatAddr_comboBox.SelectedIndex % 15, Convert.ToInt32(PIX_Sel_UpDown.Value), 1] = 16 * ((num >> 4) & 3) + (num & 0xF);
		}
		PIX_groupBox.Enabled = true;
		MAT_DCO0_Period_textBox.Enabled = false;
		MAT_DCO1_Period_textBox.Enabled = false;
		MAT_DCO_Difference_textBox.Enabled = false;
	}

	private void PIXset_change(object sender, EventArgs e)
	{
		bool flag = PIX_ON_ALL_chkBox.Checked || MAT_FE_ALL_chkBox.Checked || PIX_DCO_ALL_chkBox.Checked;
		int num = ((!flag) ? Convert.ToInt32(PIX_Sel_UpDown.Value) : 0);
		int num2 = (flag ? 63 : Convert.ToInt32(PIX_Sel_UpDown.Value));
		if (!PIX_groupBox.Enabled)
		{
			return;
		}
		Mat_dGridView.Rows[Convert.ToInt32(PIX_Sel_UpDown.Value) / 16].Cells[Convert.ToInt32(PIX_Sel_UpDown.Value) % 16].Value = (128 * Convert.ToInt32(MAT_FE_ON_chkBox.Checked) + 64 * Convert.ToInt32(PIX_ON_chkBox.Checked) + 16 * Convert.ToInt32(DCO_PIX_adj_UpDown.Value) + Convert.ToInt32(DCO_PIX_ctrl_UpDown.Value)).ToString("X2");
		for (int i = num; i <= num2; i++)
		{
			int num3 = Convert.ToInt32(Mat_dGridView.Rows[i / 16].Cells[i % 16].Value.ToString(), 16);
			int num4 = ((!MAT_FE_ALL_chkBox.Checked) ? (128 * (num3 >> 7)) : (128 * Convert.ToInt32(MAT_FE_ON_chkBox.Checked)));
			int num5 = ((!PIX_ON_ALL_chkBox.Checked) ? (64 * ((num3 >> 6) & 1)) : (64 * Convert.ToInt32(PIX_ON_chkBox.Checked)));
			int num6;
			int num7;
			if (PIX_DCO_ALL_chkBox.Checked)
			{
				num6 = 16 * Convert.ToInt32(DCO_PIX_adj_UpDown.Value);
				num7 = Convert.ToInt32(DCO_PIX_ctrl_UpDown.Value);
			}
			else
			{
				num6 = 16 * ((num3 >> 4) & 3);
				num7 = num3 & 0xF;
			}
			Mat_dGridView.Rows[i / 16].Cells[i % 16].Value = (num4 + num5 + num6 + num7).ToString("X2");
			LoadedData.DCO_ConfPairs_Matrix[Cur_Quad, MatAddr_comboBox.SelectedIndex % 15, i, 1] = num6 + num7;
		}
		MatI2C_write_range(num, num2);
	}

	private void MAT_FT_DACsetNum_change(object sender, EventArgs e)
	{
		if (sender.Equals(MAT_DAC_FT_SEL_UpDown))
		{
			Mat_dGridView.CurrentCell = Mat_dGridView[(Convert.ToInt32(MAT_DAC_FT_SEL_UpDown.Value) / 2 + 76) % 16, (Convert.ToInt32(MAT_DAC_FT_SEL_UpDown.Value) / 2 + 76) / 16];
		}
		MAT_DACset_refresh();
	}

	private void MAT_DACset_refresh()
	{
		MAT_DAC_groupBox.Enabled = false;
		int num = Convert.ToInt32(Mat_dGridView.Rows[(Convert.ToInt32(MAT_DAC_FT_SEL_UpDown.Value) / 2 + 76) / 16].Cells[(Convert.ToInt32(MAT_DAC_FT_SEL_UpDown.Value) / 2 + 76) % 16].Value.ToString(), 16);
		if (MAT_DAC_FT_SEL_UpDown.Value % 2m == 1m)
		{
			MAT_DAC_FT_UpDown.Value = (num >> 4) & 0xF;
		}
		else if (MAT_DAC_FT_SEL_UpDown.Value % 2m == 0m)
		{
			MAT_DAC_FT_UpDown.Value = num & 0xF;
		}
		num = Convert.ToInt32(Mat_dGridView.Rows[4].Cells[6].Value.ToString(), 16);
		MAT_DAC_VTH_H_EN_chkBox.Checked = Convert.ToBoolean(num & 0x80);
		MAT_DAC_VTH_H_UpDown.Value = num & 0x7F;
		num = Convert.ToInt32(Mat_dGridView.Rows[4].Cells[7].Value.ToString(), 16);
		MAT_DAC_VTH_L_EN_chkBox.Checked = Convert.ToBoolean(num & 0x80);
		MAT_DAC_VTH_L_UpDown.Value = num & 0x7F;
		num = Convert.ToInt32(Mat_dGridView.Rows[4].Cells[8].Value.ToString(), 16);
		MAT_DAC_VINJ_H_EN_chkBox.Checked = Convert.ToBoolean(num & 0x80);
		MAT_DAC_VINJ_H_UpDown.Value = num & 0x7F;
		num = Convert.ToInt32(Mat_dGridView.Rows[4].Cells[9].Value.ToString(), 16);
		MAT_DAC_VINJ_L_EN_chkBox.Checked = Convert.ToBoolean(num & 0x80);
		MAT_DAC_VINJ_L_UpDown.Value = num & 0x7F;
		num = Convert.ToInt32(Mat_dGridView.Rows[4].Cells[10].Value.ToString(), 16);
		MAT_DAC_VLDO_EN_chkBox.Checked = Convert.ToBoolean(num & 0x80);
		MAT_DAC_VLDO_UpDown.Value = num & 0x7F;
		num = Convert.ToInt32(Mat_dGridView.Rows[4].Cells[11].Value.ToString(), 16);
		MAT_DAC_VFB_EN_chkBox.Checked = Convert.ToBoolean(num & 0x80);
		MAT_DAC_VFB_UpDown.Value = num & 0x7F;
		MAT_DAC_groupBox.Enabled = true;
	}

	private void MAT_FT_DACset_change(object sender, EventArgs e)
	{
		int num = (MAT_DAC_ALL_FT_chkBox.Checked ? 76 : (Convert.ToInt32(MAT_DAC_FT_SEL_UpDown.Value) / 2 + 76));
		int num2 = (MAT_DAC_ALL_FT_chkBox.Checked ? 107 : (Convert.ToInt32(MAT_DAC_FT_SEL_UpDown.Value) / 2 + 76));
		if (!MAT_DAC_groupBox.Enabled)
		{
			return;
		}
		for (int i = num; i <= num2; i++)
		{
			int num3 = Convert.ToInt32(Mat_dGridView.Rows[i / 16].Cells[i % 16].Value.ToString(), 16);
			int num4;
			if (MAT_DAC_ALL_FT_chkBox.Checked)
			{
				num4 = 17 * Convert.ToInt32(MAT_DAC_FT_UpDown.Value);
			}
			else if (MAT_DAC_FT_SEL_UpDown.Value % 2m == 0m)
			{
				num4 = (num3 & 0xF0) + Convert.ToInt32(MAT_DAC_FT_UpDown.Value);
			}
			else if (MAT_DAC_FT_SEL_UpDown.Value % 2m == 1m)
			{
				num4 = (num3 & 0xF) + 16 * Convert.ToInt32(MAT_DAC_FT_UpDown.Value);
			}
			else
			{
				MessageBox.Show("How the fuck can an integer be neither odd or even wtf");
				num4 = 0;
			}
			Mat_dGridView.Rows[i / 16].Cells[i % 16].Value = num4.ToString("X2");
		}
		MatI2C_write_range(num, num2);
	}

	private void MAT_IN_DACset_change(object sender, EventArgs e)
	{
		if (MAT_DAC_groupBox.Enabled)
		{
			if (sender.Equals(MAT_DAC_VTH_H_UpDown) || sender.Equals(MAT_DAC_VTH_H_EN_chkBox))
			{
				Mat_dGridView.Rows[4].Cells[6].Value = (128 * Convert.ToInt32(MAT_DAC_VTH_H_EN_chkBox.Checked) + Convert.ToInt32(MAT_DAC_VTH_H_UpDown.Value)).ToString("X2");
				Mat_dGridView.CurrentCell = Mat_dGridView[6, 4];
			}
			else if (sender.Equals(MAT_DAC_VTH_L_UpDown) || sender.Equals(MAT_DAC_VTH_L_EN_chkBox))
			{
				Mat_dGridView.Rows[4].Cells[7].Value = (128 * Convert.ToInt32(MAT_DAC_VTH_L_EN_chkBox.Checked) + Convert.ToInt32(MAT_DAC_VTH_L_UpDown.Value)).ToString("X2");
				Mat_dGridView.CurrentCell = Mat_dGridView[7, 4];
			}
			else if (sender.Equals(MAT_DAC_VINJ_H_UpDown) || sender.Equals(MAT_DAC_VINJ_H_EN_chkBox))
			{
				Mat_dGridView.Rows[4].Cells[8].Value = (128 * Convert.ToInt32(MAT_DAC_VINJ_H_EN_chkBox.Checked) + Convert.ToInt32(MAT_DAC_VINJ_H_UpDown.Value)).ToString("X2");
				Mat_dGridView.CurrentCell = Mat_dGridView[8, 4];
			}
			else if (sender.Equals(MAT_DAC_VINJ_L_UpDown) || sender.Equals(MAT_DAC_VINJ_L_EN_chkBox))
			{
				Mat_dGridView.Rows[4].Cells[9].Value = (128 * Convert.ToInt32(MAT_DAC_VINJ_L_EN_chkBox.Checked) + Convert.ToInt32(MAT_DAC_VINJ_L_UpDown.Value)).ToString("X2");
				Mat_dGridView.CurrentCell = Mat_dGridView[9, 4];
			}
			else if (sender.Equals(MAT_DAC_VLDO_UpDown) || sender.Equals(MAT_DAC_VLDO_EN_chkBox))
			{
				Mat_dGridView.Rows[4].Cells[10].Value = (128 * Convert.ToInt32(MAT_DAC_VLDO_EN_chkBox.Checked) + Convert.ToInt32(MAT_DAC_VLDO_UpDown.Value)).ToString("X2");
				Mat_dGridView.CurrentCell = Mat_dGridView[10, 4];
			}
			else if (sender.Equals(MAT_DAC_VFB_UpDown) || sender.Equals(MAT_DAC_VFB_EN_chkBox))
			{
				Mat_dGridView.Rows[4].Cells[11].Value = (128 * Convert.ToInt32(MAT_DAC_VFB_EN_chkBox.Checked) + Convert.ToInt32(MAT_DAC_VFB_UpDown.Value)).ToString("X2");
				Mat_dGridView.CurrentCell = Mat_dGridView[11, 4];
			}
			MatI2C_write_range(70, 75);
		}
	}

	private async Task MAT_DAC_VLDO_Test()
	{
		saveFileDialog1.FileName = "DAC_Test_VLDO";
		if (saveFileDialog1.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		string fileName = saveFileDialog1.FileName;
		using StreamWriter writer = new StreamWriter(fileName, append: true);
		int num = Convert.ToInt32(MAT_I2C_addr_tBox.Text, 16) / 2;
		if (num != 1 && num != 3 && num != 9 && num != 11)
		{
			MessageBox.Show("Mattonella non adatta, \n seleziona tra 1, 3, 9 o 11");
			return;
		}
		Log_textBox.AppendText("\r\n" + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Log_textBox.AppendText(" ==> Starting DAC Test");
		byte devAddr = Convert.ToByte(Ignite32_MATID_ToIntDevAddr(num));
		int temp_DAC_VLDO = Convert.ToInt32(MAT_DAC_VLDO_UpDown.Value);
		int total_loops = 10;
		double avg_val = 0.0;
		string[] RowTitles = new string[6] { "Avg Vthr High", "Avg Vthr Low", "Avg Vinj High", "Avg Vref Low", "Avg V LDO", "Avg Vfeed" };
		await writer.WriteLineAsync();
		await writer.WriteAsync("DAC_Code".PadRight(15) + "\t");
		for (int DAC_code = 0; DAC_code < 128; DAC_code++)
		{
			await writer.WriteAsync(DAC_code.ToString().PadRight(10) + "\t");
		}
		await writer.WriteLineAsync();
		for (int j = 0; j < 6; j++)
		{
			await writer.WriteAsync(RowTitles[j].PadRight(15) + "\t");
			Log_textBox.AppendText("\r\nStarting Test on DAC" + RowTitles[j]);
			for (int DAC_code = 0; DAC_code < 128; DAC_code++)
			{
				byte dataByte = Convert.ToByte(128 + DAC_code);
				byte b = I2C_Guarded_WriteByte(devAddr, Convert.ToByte(70 + j), dataByte);
				if (b != 0)
				{
					MessageBox.Show(b.ToString());
				}
				for (int i = 0; i < total_loops; i++)
				{
					WriteAdc_noUI(Quad: true, j);
					await Task.Delay(3);
					avg_val += ReadAdc_noUI(Quad: true, j) / (double)total_loops;
				}
				if (DAC_code % 32 == 0)
				{
					Log_textBox.AppendText("\r\n     Test on DAC" + RowTitles[j] + " is " + DAC_code / 32 * 25 + "% done");
				}
				await writer.WriteAsync(avg_val.ToString().PadRight(10) + "\t");
				avg_val = 0.0;
			}
			Log_textBox.AppendText("\r\n     Test on DAC" + RowTitles[j] + " is 100% done");
			await writer.WriteLineAsync();
			if (j == 4)
			{
				byte b = I2C_Guarded_WriteByte(devAddr, Convert.ToByte(70 + j), Convert.ToByte(128 + temp_DAC_VLDO));
				if (b != 0)
				{
					MessageBox.Show(b.ToString());
				}
			}
		}
		Log_textBox.AppendText("\r\n" + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff "));
		Log_textBox.AppendText(" ==> Finished DAC Test");
	}

	private async void MAT_DAC_VLDO_tmp_butClick(object sender, EventArgs e)
	{
		foreach (Control control3 in Mat_tabPage.Controls)
		{
			control3.Enabled = false;
		}
		await MAT_DAC_VLDO_Test();
		MessageBox.Show("DAC VLDO Scan done");
		foreach (Control control4 in Mat_tabPage.Controls)
		{
			control4.Enabled = true;
		}
	}

	private async void MAT_AFE_ConsTest_butClick(object sender, EventArgs e)
	{
		foreach (Control control3 in Mat_tabPage.Controls)
		{
			control3.Enabled = false;
		}
		MessageBox.Show("AFE Consumption Test done");
		await Task.Delay(1);
		foreach (Control control4 in Mat_tabPage.Controls)
		{
			control4.Enabled = true;
		}
	}

	private double[] MAT_AFE_ConsTest()
	{
		return new double[4];
	}

	private void MAT_AFE_refresh()
	{
		MAT_AFE_groupBox.Enabled = false;
		int num = Convert.ToInt32(Mat_dGridView.Rows[4].Cells[3].Value.ToString(), 16);
		CON_PAD_chkBox.Checked = Convert.ToBoolean(num & 0x80);
		EN_P_VTH_chkBox.Checked = Convert.ToBoolean(num & 0x40);
		EN_P_VLDO_chkBox.Checked = Convert.ToBoolean(num & 0x20);
		EN_P_VFB_chkBox.Checked = Convert.ToBoolean(num & 0x10);
		num = Convert.ToInt32(Mat_dGridView.Rows[4].Cells[4].Value.ToString(), 16);
		AFE_LB_chkBox.Checked = Convert.ToBoolean(num & 0x80);
		AFE_AUTO_chkBox.Checked = Convert.ToBoolean(num & 0x40);
		DAC_IDISC_UpDown.Value = (num >> 3) & 7;
		DAC_ICSA_UpDown.Value = num & 7;
		num = Convert.ToInt32(Mat_dGridView.Rows[4].Cells[5].Value.ToString(), 16);
		EXT_DC_chkBox.Checked = Convert.ToBoolean(num & 0x80);
		EN_P_VINJ_chkBox.Checked = Convert.ToBoolean(num & 0x40);
		SEL_VINJ_MUX_Low_comboBox.SelectedIndex = (num >> 4) & 1;
		SEL_VINJ_MUX_High_comboBox.SelectedIndex = (num >> 5) & 1;
		DAC_IKRUM_UpDown.Value = num & 0xF;
		MAT_AFE_groupBox.Enabled = true;
	}

	private void MAT_AFE_change(object sender, EventArgs e)
	{
		if (!MAT_AFE_groupBox.Enabled)
		{
			return;
		}
		if (sender.Equals(CON_PAD_chkBox) || sender.Equals(EN_P_VTH_chkBox) || sender.Equals(EN_P_VLDO_chkBox) || sender.Equals(EN_P_VFB_chkBox))
		{
			int selectedIndex = MatAddr_comboBox.SelectedIndex;
			bool flag = CON_PAD_chkBox.Checked;
			bool flag2 = EN_P_VTH_chkBox.Checked;
			bool flag3 = EN_P_VLDO_chkBox.Checked;
			bool flag4 = EN_P_VFB_chkBox.Checked;
			if ((selectedIndex == 1 || selectedIndex == 3 || selectedIndex == 9 || selectedIndex == 11 || selectedIndex == 16) && (flag || flag2 || flag3 || flag4))
			{
				Ignite32_MAT_Write_Single(1, 67, 0, 4, 4);
				Ignite32_MAT_Write_Single(3, 67, 0, 4, 4);
				Ignite32_MAT_Write_Single(9, 67, 0, 4, 4);
				Ignite32_MAT_Write_Single(11, 67, 0, 4, 4);
				Ignite32_IOext_Write_Single(10, 1, 1, 1);
			}
			int num = Convert.ToInt32(Mat_dGridView.Rows[4].Cells[3].Value.ToString(), 16);
			Mat_dGridView.Rows[4].Cells[3].Value = (Convert.ToInt32(flag) * 128 + Convert.ToInt32(flag2) * 64 + Convert.ToInt32(flag3) * 32 + Convert.ToInt32(flag4) * 16 + (num & 0xF)).ToString("X2");
			Mat_dGridView.CurrentCell = Mat_dGridView[3, 4];
			MatI2C_write_single_but_Click(sender, e);
		}
		else if (sender.Equals(AFE_LB_chkBox) || sender.Equals(AFE_AUTO_chkBox) || sender.Equals(DAC_IDISC_UpDown) || sender.Equals(DAC_ICSA_UpDown))
		{
			Mat_dGridView.Rows[4].Cells[4].Value = (128 * Convert.ToInt32(AFE_LB_chkBox.Checked) + 64 * Convert.ToInt32(AFE_AUTO_chkBox.Checked) + 8 * Convert.ToInt32(DAC_IDISC_UpDown.Value) + Convert.ToInt32(DAC_ICSA_UpDown.Value)).ToString("X2");
			Mat_dGridView.CurrentCell = Mat_dGridView[4, 4];
			MatI2C_write_single_but_Click(sender, e);
		}
		else if (sender.Equals(EXT_DC_chkBox) || sender.Equals(EN_P_VINJ_chkBox) || sender.Equals(SEL_VINJ_MUX_High_comboBox) || sender.Equals(SEL_VINJ_MUX_Low_comboBox) || sender.Equals(DAC_IKRUM_UpDown))
		{
			Mat_dGridView.Rows[4].Cells[5].Value = (128 * Convert.ToInt32(EXT_DC_chkBox.Checked) + 64 * Convert.ToInt32(EN_P_VINJ_chkBox.Checked) + 32 * Convert.ToInt32(SEL_VINJ_MUX_High_comboBox.SelectedIndex) + 16 * Convert.ToInt32(SEL_VINJ_MUX_Low_comboBox.SelectedIndex) + Convert.ToInt32(DAC_IKRUM_UpDown.Value)).ToString("X2");
			Mat_dGridView.CurrentCell = Mat_dGridView[5, 4];
			MatI2C_write_single_but_Click(sender, e);
		}
	}

	private void IOextAddr_comboBox_SelectedIndexChanged(object sender, EventArgs e)
	{
		switch (IOextAddr_comboBox.SelectedIndex)
		{
		case 0:
			IOext_I2C_addr_tBox.Text = "40";
			IOext_I2C_addr_tBox.Enabled = false;
			break;
		case 1:
			IOext_I2C_addr_tBox.Text = "AE";
			IOext_I2C_addr_tBox.Enabled = false;
			break;
		case 2:
			IOext_I2C_addr_tBox.Text = "00";
			IOext_I2C_addr_tBox.Enabled = true;
			break;
		}
	}

	private void IOext_init()
	{
		for (int i = 0; i <= 10; i++)
		{
			switch (i)
			{
			case 0:
				IOext_dGridView.Rows[0].Cells[i].Value = "80";
				break;
			case 5:
				IOext_dGridView.Rows[0].Cells[i].Value = "20";
				break;
			case 9:
				IOext_dGridView.Rows[0].Cells[i].Value = "34";
				break;
			case 10:
				IOext_dGridView.Rows[0].Cells[i].Value = "40";
				break;
			default:
				IOext_dGridView.Rows[0].Cells[i].Value = "00";
				break;
			}
			IOext_dGridView.CurrentCell = IOext_dGridView[i, 0];
			IOextI2C_write_single_but_Click(IOextI2C_write_single_but, EventArgs.Empty);
		}
		IOext_gpio_refresh();
	}

	private void IOextI2C_write_single_but_Click(object sender, EventArgs e)
	{
		I2C_oper_tSSLabel.Text = "Writing...";
		int value = Convert.ToInt32(IOext_I2C_addr_tBox.Text, 16);
		int value2 = 16 * IOext_dGridView.CurrentRow.Index + IOext_dGridView.CurrentCell.ColumnIndex;
		byte b = 0;
		int value3 = Convert.ToInt32(IOext_dGridView.SelectedCells[0].Value.ToString(), 16);
		if (!debug)
		{
			b = I2C_Guarded_WriteByte(Convert.ToByte(value), Convert.ToByte(value2), Convert.ToByte(value3));
		}
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
		IOext_gpio_refresh();
	}

	private void IOextI2C_read_single_but_Click(object sender, EventArgs e)
	{
		I2C_oper_tSSLabel.Text = "Reading...";
		int value = Convert.ToInt32(IOext_I2C_addr_tBox.Text, 16);
		int value2 = 16 * IOext_dGridView.CurrentRow.Index + IOext_dGridView.CurrentCell.ColumnIndex;
		byte ReadData;
		byte b = I2C_Guarded_ReadByte(Convert.ToByte(value), Convert.ToByte(value2), out ReadData);
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
			IOext_dGridView.SelectedCells[0].Value = ReadData.ToString("X2");
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
		IOext_gpio_refresh();
	}

	private void IOextI2C_write_all_but_Click(object sender, EventArgs e)
	{
		for (int i = 0; i <= 10; i++)
		{
			IOext_dGridView.CurrentCell = IOext_dGridView[i, 0];
			IOextI2C_write_single_but_Click(sender, EventArgs.Empty);
		}
	}

	private void IOextI2C_read_all_but_Click(object sender, EventArgs e)
	{
		for (int i = 0; i <= 10; i++)
		{
			IOext_dGridView.CurrentCell = IOext_dGridView[i, 0];
			IOextI2C_read_single_but_Click(sender, EventArgs.Empty);
		}
	}

	private void IOext_gpio_refresh()
	{
		int num = Convert.ToInt32(IOext_dGridView.Rows[0].Cells[10].Value.ToString(), 16);
		FastinSrc_comboBox.SelectedIndex = num & 1;
		ExtDacEn_chkBox.Checked = ((((num >> 1) & 1) != 0) ? true : false);
		SelDataEnSrc_comboBox.SelectedIndex = (num >> 2) & 1;
		SICLKOE_chkBox.Checked = ((((num >> 3) & 1) != 0) ? true : false);
		SiClkInSrc_comboBox.SelectedIndex = (num >> 4) & 3;
		AnaPwr_chkBox.Checked = ((num >> 6) & 1) != 1;
		SiLol_chkBox.Checked = ((((num >> 7) & 1) != 0) ? true : false);
		AnalogPowerStatus_tSSLabel.Text = (AnaPwr_chkBox.Checked ? "Analog Pwr: ON" : "Analog Pwr: OFF");
		AnalogPowerStatus_tSSLabel.BackColor = (AnaPwr_chkBox.Checked ? Color.LightGreen : Color.Red);
	}

	private void IOext_gpio_Write_but_Click(object sender, EventArgs e)
	{
		int num = 0;
		int num2 = ((!AnaPwr_chkBox.Checked) ? 1 : 0);
		num = 128 * Convert.ToInt32(SiLol_chkBox.Checked) + 64 * num2 + 16 * SiClkInSrc_comboBox.SelectedIndex + 8 * Convert.ToInt32(SICLKOE_chkBox.Checked) + 4 * SelDataEnSrc_comboBox.SelectedIndex + 2 * Convert.ToInt32(ExtDacEn_chkBox.Checked) + FastinSrc_comboBox.SelectedIndex;
		IOext_dGridView.Rows[0].Cells[10].Value = num.ToString("X2");
		IOext_dGridView.CurrentCell = IOext_dGridView[10, 0];
		IOextI2C_write_single_but_Click(sender, EventArgs.Empty);
	}

	private void IOext_gpio_Read_but_Click(object sender, EventArgs e)
	{
		IOext_gpio_refresh();
	}

	private void I2CmuxAddr_comboBox_SelectedIndexChanged(object sender, EventArgs e)
	{
		switch (I2CmuxAddr_comboBox.SelectedIndex)
		{
		case 0:
			Mux_I2C_addr_tBox.Text = "E0";
			Mux_I2C_addr_tBox.Enabled = false;
			break;
		case 1:
			Mux_I2C_addr_tBox.Text = "AE";
			Mux_I2C_addr_tBox.Enabled = false;
			break;
		case 2:
			Mux_I2C_addr_tBox.Text = "00";
			Mux_I2C_addr_tBox.Enabled = true;
			break;
		}
	}

	private void Mux_I2C_write_but_Click(object sender, EventArgs e)
	{
		I2C_oper_tSSLabel.Text = "Writing...";
		int value = Convert.ToInt32(Mux_I2C_addr_tBox.Text, 16);
		byte b = 0;
		int num = Convert.ToInt32(CtrlReg_tBox.Text, 16);
		if (Math.Sqrt(num) < 4.0 && Math.Sqrt(num) > 0.0)
		{
			Cur_Quad = Convert.ToInt32(Math.Sqrt(num));
		}
		if (num == 1)
		{
			Cur_Quad = 0;
		}
		if (!debug)
		{
			b = I2C_Guarded_SendByte(Convert.ToByte(value), Convert.ToByte(num));
		}
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
		CtrlReg_tBox.Text = num.ToString("X2");
		I2Cmux_refresh();
	}

	private void Mux_I2C_read_but_Click(object sender, EventArgs e)
	{
		I2C_oper_tSSLabel.Text = "Reading...";
		int value = Convert.ToInt32(Mux_I2C_addr_tBox.Text, 16);
		byte ReadData;
		byte b = I2C_ReceiveByte(Convert.ToByte(value), out ReadData);
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
			IOext_dGridView.SelectedCells[0].Value = ReadData.ToString("X2");
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
		CtrlReg_tBox.Text = ReadData.ToString("X2");
		I2Cmux_refresh();
	}

	private void Quad_I2C_Mux_CheckedChange(object sender, EventArgs e)
	{
		if (I2Cmux_grpBox.Enabled)
		{
			int num = (sender.Equals(SW_i2c_chkBox) ? Convert.ToInt32(SW_i2c_chkBox.Checked) : 0);
			int num2 = (sender.Equals(NW_i2c_chkBox) ? (2 * Convert.ToInt32(NW_i2c_chkBox.Checked)) : 0);
			int num3 = (sender.Equals(SE_i2c_chkBox) ? (4 * Convert.ToInt32(SE_i2c_chkBox.Checked)) : 0);
			int num4 = (sender.Equals(NE_i2c_chkBox) ? (8 * Convert.ToInt32(NE_i2c_chkBox.Checked)) : 0);
			int num5 = 0;
			num5 += num4;
			num5 += num3;
			num5 += num2;
			CtrlReg_tBox.Text = (num5 + num).ToString("X2");
			Cur_Quad = new int[4]
			{
				num - 1,
				num2 / 2,
				num3 / 2,
				num4 / 2 - 1
			}.Max();
			Mux_I2C_write_but_Click(sender, EventArgs.Empty);
		}
	}

	private void I2Cmux_refresh()
	{
		I2Cmux_grpBox.Enabled = false;
		string text = "Mux Status: ";
		int num = Convert.ToInt32(CtrlReg_tBox.Text, 16);
		SW_i2c_chkBox.Checked = (((num & 1) != 0) ? true : false);
		NW_i2c_chkBox.Checked = ((((num >> 1) & 1) != 0) ? true : false);
		SE_i2c_chkBox.Checked = ((((num >> 2) & 1) != 0) ? true : false);
		NE_i2c_chkBox.Checked = ((((num >> 3) & 1) != 0) ? true : false);
		TopSelQuad_SW_chkBox.Checked = SW_i2c_chkBox.Checked;
		MatSelQuad_SW_chkBox.Checked = SW_i2c_chkBox.Checked;
		ExtSelQuad_SW_chkBox.Checked = SW_i2c_chkBox.Checked;
		TopSelQuad_NW_chkBox.Checked = NW_i2c_chkBox.Checked;
		MatSelQuad_NW_chkBox.Checked = NW_i2c_chkBox.Checked;
		ExtSelQuad_NW_chkBox.Checked = NW_i2c_chkBox.Checked;
		TopSelQuad_SE_chkBox.Checked = SE_i2c_chkBox.Checked;
		MatSelQuad_SE_chkBox.Checked = SE_i2c_chkBox.Checked;
		ExtSelQuad_SE_chkBox.Checked = SE_i2c_chkBox.Checked;
		TopSelQuad_NE_chkBox.Checked = NE_i2c_chkBox.Checked;
		MatSelQuad_NE_chkBox.Checked = NE_i2c_chkBox.Checked;
		ExtSelQuad_NE_chkBox.Checked = NE_i2c_chkBox.Checked;
		MuxStatus_tSSLabel.BackColor = Color.Yellow;
		if (NE_i2c_chkBox.Checked)
		{
			text += "NE ";
			MuxStatus_tSSLabel.BackColor = SystemColors.Control;
		}
		if (SE_i2c_chkBox.Checked)
		{
			text += "SE ";
			MuxStatus_tSSLabel.BackColor = SystemColors.Control;
		}
		if (NW_i2c_chkBox.Checked)
		{
			text += "NW ";
			MuxStatus_tSSLabel.BackColor = SystemColors.Control;
		}
		if (SW_i2c_chkBox.Checked)
		{
			text += "SW ";
			MuxStatus_tSSLabel.BackColor = SystemColors.Control;
		}
		MuxStatus_tSSLabel.Text = text;
		I2Cmux_grpBox.Enabled = true;
	}

	private void WriteDac_Click(object sender, EventArgs e)
	{
		int num = 0;
		int num2 = 0;
		byte[] array = new byte[3];
		int value = 32;
		byte b = 0;
		if (sender.Equals(DACvthr_H_but))
		{
			if (Convert.ToDouble(DACvthr_H_UpDown.Value) > Convert.ToDouble(VDDA_txtBox.Text))
			{
				DACvthr_H_UpDown.Value = Convert.ToDecimal(VDDA_txtBox.Text);
			}
			num = Convert.ToInt32(Convert.ToDouble(DACvthr_H_UpDown.Value) * (Math.Pow(2.0, 16.0) - 1.0) / Convert.ToDouble(VDDA_txtBox.Text));
			value = 192;
			num2 = 0;
		}
		else if (sender.Equals(DACvthr_L_but))
		{
			if (Convert.ToDouble(DACvthr_L_UpDown.Value) > Convert.ToDouble(VDDA_txtBox.Text))
			{
				DACvthr_L_UpDown.Value = Convert.ToDecimal(VDDA_txtBox.Text);
			}
			num = Convert.ToInt32(Convert.ToDouble(DACvthr_L_UpDown.Value) * (Math.Pow(2.0, 16.0) - 1.0) / Convert.ToDouble(VDDA_txtBox.Text));
			value = 192;
			num2 = 1;
		}
		else if (sender.Equals(DACvinj_H_but))
		{
			if (Convert.ToDouble(DACvinj_H_UpDown.Value) > Convert.ToDouble(VDDA_txtBox.Text))
			{
				DACvinj_H_UpDown.Value = Convert.ToDecimal(VDDA_txtBox.Text);
			}
			num = Convert.ToInt32(Convert.ToDouble(DACvinj_H_UpDown.Value) * (Math.Pow(2.0, 16.0) - 1.0) / Convert.ToDouble(VDDA_txtBox.Text));
			value = 192;
			num2 = 2;
		}
		else if (sender.Equals(DACvref_L_but))
		{
			if (Convert.ToDouble(DACvref_L_UpDown.Value) > Convert.ToDouble(VDDA_txtBox.Text))
			{
				DACvref_L_UpDown.Value = Convert.ToDecimal(VDDA_txtBox.Text);
			}
			num = Convert.ToInt32(Convert.ToDouble(DACvref_L_UpDown.Value) * (Math.Pow(2.0, 16.0) - 1.0) / Convert.ToDouble(VDDA_txtBox.Text));
			value = 192;
			num2 = 3;
		}
		else if (sender.Equals(DACVext_but))
		{
			if (Convert.ToDouble(DACVext_UpDown.Value) > Convert.ToDouble(VDDA_txtBox.Text))
			{
				DACVext_UpDown.Value = Convert.ToDecimal(VDDA_txtBox.Text);
			}
			num = Convert.ToInt32(Convert.ToDouble(DACVext_UpDown.Value) * (Math.Pow(2.0, 16.0) - 1.0) / Convert.ToDouble(VDDA_txtBox.Text));
			value = 194;
			num2 = 0;
		}
		else if (sender.Equals(DACvfeed_but))
		{
			if (Convert.ToDouble(DACvfeed_UpDown.Value) > Convert.ToDouble(VDDA_txtBox.Text))
			{
				DACvfeed_UpDown.Value = Convert.ToDecimal(VDDA_txtBox.Text);
			}
			num = Convert.ToInt32(Convert.ToDouble(DACvfeed_UpDown.Value) * (Math.Pow(2.0, 16.0) - 1.0) / Convert.ToDouble(VDDA_txtBox.Text));
			value = 32;
			num2 = 0;
		}
		else if (sender.Equals(DACiref_but))
		{
			if (Convert.ToDouble(DACiref_UpDown.Value) > Convert.ToDouble(VDDA_txtBox.Text))
			{
				DACiref_UpDown.Value = Convert.ToDecimal(VDDA_txtBox.Text);
			}
			num = Convert.ToInt32(Convert.ToDouble(DACiref_UpDown.Value) * (Math.Pow(2.0, 16.0) - 1.0) / Convert.ToDouble(VDDA_txtBox.Text));
			value = 32;
			num2 = 1;
		}
		else if (sender.Equals(DACicap_but))
		{
			if (Convert.ToDouble(DACicap_UpDown.Value) > Convert.ToDouble(VDDA_txtBox.Text))
			{
				DACicap_UpDown.Value = Convert.ToDecimal(VDDA_txtBox.Text);
			}
			num = Convert.ToInt32(Convert.ToDouble(DACicap_UpDown.Value) * (Math.Pow(2.0, 16.0) - 1.0) / Convert.ToDouble(VDDA_txtBox.Text));
			value = 32;
			num2 = 2;
		}
		array[0] = Convert.ToByte(48 + num2);
		array[1] = Convert.ToByte((num >> 8) & 0xFF);
		array[2] = Convert.ToByte(num & 0xFF);
		if (!debug)
		{
			b = I2C_Write(Convert.ToByte(value), 3, array, 1);
		}
		if (b == 0)
		{
			I2C_oper_tSSLabel.Text = "Transmission OK";
		}
		else
		{
			MessageBox.Show(b.ToString());
		}
	}

	private void WriteAdc_Click(object sender, EventArgs e)
	{
		byte b = 0;
		int num = 212;
		int num2 = 0;
		byte b2 = 0;
		if (sender.Equals(ADCcommon_Write_but))
		{
			if (ADCcommon_ch_comboBox.SelectedIndex <= 3)
			{
				num = 212;
				num2 = ADCcommon_ch_comboBox.SelectedIndex;
			}
			else
			{
				num = 214;
				num2 = ADCcommon_ch_comboBox.SelectedIndex - 4;
			}
			b = Convert.ToByte(128 * Convert.ToInt32(ADCcommon_RDY_chkBox.Checked) + 32 * num2 + 16 * Convert.ToInt32(ADCcommon_OC_chkBox.Checked) + 4 * ADCcommon_Res_comboBox.SelectedIndex + ADCcommon_Gain_comboBox.SelectedIndex);
			if (!debug)
			{
				b2 = I2C_Guarded_SendByte(Convert.ToByte(num), b);
			}
			if (b2 == 0)
			{
				I2C_oper_tSSLabel.Text = "Transmission OK";
			}
			else
			{
				MessageBox.Show(b2.ToString());
			}
		}
		else if (sender.Equals(ADCdac_Quad_Write_but))
		{
			if (ADCdac_Quad_ch_comboBox.SelectedIndex <= 3)
			{
				num = 208;
				num2 = ADCdac_Quad_ch_comboBox.SelectedIndex;
			}
			else
			{
				num = 210;
				num2 = ADCdac_Quad_ch_comboBox.SelectedIndex - 4;
			}
			b = Convert.ToByte(128 * Convert.ToInt32(ADCdac_Quad_RDY_chkBox.Checked) + 32 * num2 + 16 * Convert.ToInt32(ADCdac_Quad_OC_chkBox.Checked) + 4 * ADCdac_Quad_Res_comboBox.SelectedIndex + ADCdac_Quad_Gain_comboBox.SelectedIndex);
			if (!debug)
			{
				b2 = I2C_Guarded_SendByte(Convert.ToByte(num), b);
			}
			if (b2 == 0)
			{
				I2C_oper_tSSLabel.Text = "Transmission OK";
			}
			else
			{
				MessageBox.Show(b2.ToString());
			}
		}
	}

	private void OneShotAdc_Click(object sender, EventArgs e)
	{
		int millisecondsDelay = 10;
		if (sender.Equals(ADCcommon_1Shot_but))
		{
			ADCcommon_RDY_chkBox.Checked = true;
			ADCcommon_OC_chkBox.Checked = false;
			WriteAdc_Click(ADCcommon_Write_but, EventArgs.Empty);
			Task.Delay(millisecondsDelay).Wait();
			ReadAdc_Click(ADCcommon_Read_but, EventArgs.Empty);
		}
		else if (sender.Equals(ADCdac_Quad_1Shot_but))
		{
			ADCdac_Quad_RDY_chkBox.Checked = true;
			ADCdac_Quad_OC_chkBox.Checked = false;
			WriteAdc_Click(ADCdac_Quad_Write_but, EventArgs.Empty);
			Task.Delay(millisecondsDelay).Wait();
			ReadAdc_Click(ADCdac_Quad_Read_but, EventArgs.Empty);
		}
	}

	private void ReadAdc_Click(object sender, EventArgs e)
	{
		byte[] array = new byte[3];
		int num = 208;
		double[] array2 = new double[3] { 1.0, 0.25, 0.0625 };
		if (sender.Equals(ADCcommon_Read_but))
		{
			num = ((ADCcommon_ch_comboBox.SelectedIndex > 3) ? 214 : 212);
			byte b = I2C_Guarded_Read(Convert.ToByte(num), 3, array, 1);
			if (b == 0)
			{
				I2C_oper_tSSLabel.Text = "Transmission OK";
			}
			else
			{
				MessageBox.Show(b.ToString());
			}
			int num2 = 256 * array[0] + array[1];
			ADCcommon_DataHex_tBox.Text = num2.ToString("X4");
			ADCcommon_DataDec_tBox.Text = num2.ToString();
			ADCcommon_Config_tBox.Text = array[2].ToString("X2");
			ADCcommon_RDY_chkBox.Checked = Convert.ToBoolean((Convert.ToInt32(array[2]) >> 7) & 1);
			ADCcommon_ch_comboBox.SelectedIndex = ((ADCcommon_ch_comboBox.SelectedIndex <= 3) ? ((Convert.ToInt32(array[2]) >> 5) & 3) : (((Convert.ToInt32(array[2]) >> 5) & 3) + 4));
			ADCcommon_OC_chkBox.Checked = Convert.ToBoolean((Convert.ToInt32(array[2]) >> 4) & 1);
			ADCcommon_Res_comboBox.SelectedIndex = (Convert.ToInt32(array[2]) >> 2) & 3;
			ADCcommon_Gain_comboBox.SelectedIndex = Convert.ToInt32(array[2]) & 3;
			ADCcommon_Value_tBox.Text = ((double)num2 * array2[ADCcommon_Res_comboBox.SelectedIndex] / Math.Pow(2.0, ADCcommon_Gain_comboBox.SelectedIndex)).ToString();
		}
		else if (sender.Equals(ADCdac_Quad_Read_but))
		{
			num = ((ADCdac_Quad_ch_comboBox.SelectedIndex > 3) ? 210 : 208);
			byte b = I2C_Guarded_Read(Convert.ToByte(num), 3, array, 1);
			if (b == 0)
			{
				I2C_oper_tSSLabel.Text = "Transmission OK";
			}
			else
			{
				MessageBox.Show(b.ToString());
			}
			int num2 = 256 * array[0] + array[1];
			ADCdac_Quad_DataHex_tBox.Text = num2.ToString("X4");
			ADCdac_Quad_DataDec_tBox.Text = num2.ToString();
			ADCdac_Quad_Config_tBox.Text = array[2].ToString("X2");
			ADCdac_Quad_RDY_chkBox.Checked = Convert.ToBoolean((Convert.ToInt32(array[2]) >> 7) & 1);
			ADCdac_Quad_ch_comboBox.SelectedIndex = ((ADCdac_Quad_ch_comboBox.SelectedIndex <= 3) ? ((Convert.ToInt32(array[2]) >> 5) & 3) : (((Convert.ToInt32(array[2]) >> 5) & 3) + 4));
			ADCdac_Quad_OC_chkBox.Checked = Convert.ToBoolean((Convert.ToInt32(array[2]) >> 4) & 1);
			ADCdac_Quad_Res_comboBox.SelectedIndex = (Convert.ToInt32(array[2]) >> 2) & 3;
			ADCdac_Quad_Gain_comboBox.SelectedIndex = Convert.ToInt32(array[2]) & 3;
			ADCdac_Quad_Value_tBox.Text = ((double)num2 * array2[ADCdac_Quad_Res_comboBox.SelectedIndex] / Math.Pow(2.0, ADCdac_Quad_Gain_comboBox.SelectedIndex)).ToString();
		}
	}

	private void NShotAdc_Click(object sender, EventArgs e)
	{
		if (sender.Equals(ADCcommon_NShot_button))
		{
			int n_mes = Convert.ToInt32(ADCcommon_NShot_numUpDown.Value);
			int selectedIndex = ADCcommon_ch_comboBox.SelectedIndex;
			int selectedIndex2 = ADCcommon_Gain_comboBox.SelectedIndex;
			double num = 0.0;
			num = Math.Round(NShotAdc_noUI(Quad: false, selectedIndex, n_mes, selectedIndex2), 4);
			ADCcommon_Value_tBox.Text = num.ToString();
		}
		else if (sender.Equals(ADCdac_Quad_NShot_button))
		{
			int n_mes2 = Convert.ToInt32(ADCdac_Quad_NShot_numUpDown.Value);
			int selectedIndex3 = ADCdac_Quad_ch_comboBox.SelectedIndex;
			int selectedIndex4 = ADCdac_Quad_Gain_comboBox.SelectedIndex;
			double num2 = 0.0;
			num2 = Math.Round(NShotAdc_noUI(Quad: true, selectedIndex3, n_mes2, selectedIndex4), 4);
			ADCdac_Quad_Value_tBox.Text = num2.ToString();
		}
	}

	private void Ext_Quad_Sel_Change(object sender, EventArgs e)
	{
		if (sender.Equals(ExtSelQuad_SW_chkBox))
		{
			SW_i2c_chkBox.Checked = ExtSelQuad_SW_chkBox.Checked;
		}
		else if (sender.Equals(ExtSelQuad_NW_chkBox))
		{
			NW_i2c_chkBox.Checked = ExtSelQuad_NW_chkBox.Checked;
		}
		else if (sender.Equals(ExtSelQuad_NE_chkBox))
		{
			NE_i2c_chkBox.Checked = ExtSelQuad_NE_chkBox.Checked;
		}
		else if (sender.Equals(ExtSelQuad_SE_chkBox))
		{
			SE_i2c_chkBox.Checked = ExtSelQuad_SE_chkBox.Checked;
		}
	}

	private void WriteAdc_noUI(bool Quad = true, int target_Adc = 0, int gain = 0)
	{
		byte b = 0;
		int num = 212;
		int num2 = 0;
		byte b2 = 0;
		if (!Quad)
		{
			if (target_Adc <= 3)
			{
				num = 212;
				num2 = target_Adc;
			}
			else
			{
				num = 214;
				num2 = target_Adc - 4;
			}
			b = Convert.ToByte(128 * Convert.ToInt32(value: true) + 32 * num2 + 16 * Convert.ToInt32(value: false) + 8 + gain);
			if (!debug)
			{
				b2 = I2C_Guarded_SendByte(Convert.ToByte(num), b);
			}
			if (b2 == 0)
			{
				I2C_oper_tSSLabel.Text = "Transmission OK";
			}
			else
			{
				MessageBox.Show(b2.ToString());
			}
		}
		else if (Quad)
		{
			if (target_Adc <= 3)
			{
				num = 208;
				num2 = target_Adc;
			}
			else
			{
				num = 210;
				num2 = target_Adc - 4;
			}
			b = Convert.ToByte(128 * Convert.ToInt32(value: true) + 32 * num2 + 16 * Convert.ToInt32(value: false) + 8 + gain);
			if (!debug)
			{
				b2 = I2C_Guarded_SendByte(Convert.ToByte(num), b);
			}
			if (b2 == 0)
			{
				I2C_oper_tSSLabel.Text = "Transmission OK";
			}
			else
			{
				MessageBox.Show(b2.ToString());
			}
		}
	}

	private double ReadAdc_noUI(bool Quad = true, int target_Adc = 0)
	{
		byte[] array = new byte[3];
		int num = 208;
		double[] array2 = new double[3] { 1.0, 0.25, 0.0625 };
		if (!Quad)
		{
			num = ((target_Adc > 3) ? 214 : 212);
			byte b = I2C_Guarded_Read(Convert.ToByte(num), 3, array, 1);
			if (b == 0)
			{
				I2C_oper_tSSLabel.Text = "Transmission OK";
			}
			else
			{
				MessageBox.Show(b.ToString());
			}
			int num2 = 256 * array[0] + array[1];
			ADCcommon_DataHex_tBox.Text = num2.ToString("X4");
			ADCcommon_DataDec_tBox.Text = num2.ToString();
			ADCcommon_Config_tBox.Text = array[2].ToString("X2");
			return (double)num2 * array2[2] / Math.Pow(2.0, 0.0);
		}
		if (Quad)
		{
			num = ((target_Adc > 3) ? 210 : 208);
			byte b = I2C_Guarded_Read(Convert.ToByte(num), 3, array, 1);
			if (b == 0)
			{
				I2C_oper_tSSLabel.Text = "Transmission OK";
			}
			else
			{
				MessageBox.Show(b.ToString());
			}
			int num2 = 256 * array[0] + array[1];
			ADCdac_Quad_DataHex_tBox.Text = num2.ToString("X4");
			ADCdac_Quad_DataDec_tBox.Text = num2.ToString();
			ADCdac_Quad_Config_tBox.Text = array[2].ToString("X2");
			return (double)num2 * array2[2] / Math.Pow(2.0, 0.0);
		}
		MessageBox.Show("Pasta al pesto");
		return 0.0;
	}

	private double NShotAdc_noUI(bool Quad = true, int target_Adc = 0, int N_mes = 1, int gain = 0)
	{
		double num = 0.0;
		if (N_mes < 1)
		{
			N_mes = 1;
		}
		for (int i = 0; i < N_mes; i++)
		{
			WriteAdc_noUI(Quad, target_Adc, gain);
			Task.Delay(2).Wait();
			num += ReadAdc_noUI(Quad, target_Adc) / ((double)N_mes * Math.Pow(2.0, gain));
		}
		return num;
	}

	private void Global_ONorOFF_but_Click(object sender, EventArgs e)
	{
		if (sender.Equals(AllMAT_AllPIX_ON_but))
		{
			bool? pIX = true;
			AllMAT_turn_ONorOFF_all(null, null, pIX);
		}
		else if (sender.Equals(AllMAT_AllPIX_OFF_but))
		{
			bool? pIX = false;
			AllMAT_turn_ONorOFF_all(null, null, pIX);
		}
		else if (sender.Equals(AllMAT_AllTDC_ON_but))
		{
			AllMAT_turn_ONorOFF_all(true);
		}
		else if (sender.Equals(AllMAT_AllTDC_OFF_but))
		{
			AllMAT_turn_ONorOFF_all(false);
		}
		else if (sender.Equals(AllMAT_AllAFE_ON_but))
		{
			bool? pIX = true;
			AllMAT_turn_ONorOFF_all(null, pIX);
		}
		else if (sender.Equals(AllMAT_AllAFE_OFF_but))
		{
			bool? pIX = false;
			AllMAT_turn_ONorOFF_all(null, pIX);
		}
	}

	private void AllMAT_turn_ONorOFF_all(bool? TDC = null, bool? FE = null, bool? PIX = null)
	{
		for (int i = 0; i < 16; i++)
		{
			if (i <= 3 || i >= 8)
			{
				if (PIX.HasValue)
				{
					int matID = i;
					bool? pIXON = PIX;
					Ignite32_Mat_allPIX_conf_noUI(matID, null, pIXON);
				}
				else if (TDC.HasValue)
				{
					int matID2 = i;
					bool? pIXON = TDC;
					Ignite32_Mat_TDC_DCO0conf_noUI(matID2, null, pIXON);
				}
				else if (FE.HasValue)
				{
					Ignite32_Mat_allPIX_conf_noUI(i, FE);
				}
			}
		}
	}

	public object[] RawDataToObjArray(ulong DataFifoRaw, int NumberOfFields = 14)
	{
		object[] array = new object[NumberOfFields];
		array[0] = DataFifoRaw.ToString("X16");
		array[1] = (int)((DataFifoRaw >> 47) & 1);
		array[2] = (int)((DataFifoRaw >> 48) & 0xFF);
		array[3] = (int)((DataFifoRaw >> 43) & 0xF);
		array[4] = (int)(((DataFifoRaw >> 40) & 7) * 8 + ((DataFifoRaw >> 37) & 7));
		array[5] = (int)((DataFifoRaw >> 36) & 1);
		array[6] = (int)((DataFifoRaw >> 35) & 1);
		array[7] = (int)((DataFifoRaw >> 26) & 0x1FF);
		if (Convert.ToBoolean((DataFifoRaw >> 35) & 1))
		{
			array[8] = (int)((DataFifoRaw >> 16) & 1);
			array[9] = (int)((DataFifoRaw >> 15) & 1);
			array[10] = (int)((DataFifoRaw >> 13) & 3);
			array[11] = "-";
			array[12] = "-";
			array[13] = (int)(DataFifoRaw & 0x1FFF);
		}
		else
		{
			array[8] = "-";
			array[9] = "-";
			array[10] = "-";
			array[11] = (int)((DataFifoRaw >> 17) & 0x1FF);
			array[12] = (int)((DataFifoRaw >> 8) & 0x1FF);
			array[13] = (int)(DataFifoRaw & 0xFF);
		}
		return array;
	}

	private async Task<int> ReadUntilEmpty()
	{
		int n_read = 0;
		bool keep_going = true;
		while (keep_going)
		{
			ulong num = FifoReadSingle();
			int num2 = Convert.ToInt32((num >> 48) & 0xFF);
			if (num2 <= 1)
			{
				keep_going = false;
				continue;
			}
			n_read++;
			await Task.Delay(1);
		}
		return n_read;
	}

	private async Task<int> ReadUntilEmpty(ushort adjctrlDCO0, ushort adjctrlDCO1, bool new_cal = false, int TA_C = 999, int TOT_C = 999, int threshold = 1023)
	{
		int n_read = 0;
		bool keep_going = true;
		ushort cons_err = 0;
		while (keep_going)
		{
			ulong num = await Task.Run(() => FifoReadSingle());
			if (num == 0L)
			{
				n_read++;
				cons_err++;
				continue;
			}
			if (n_read % 4 == 0)
			{
				cons_err = 0;
			}
			if (cons_err == 3)
			{
				MessageBox.Show("Error code 3\r\nHappened 3 consecutive times\r\nReadUntilEmpty");
				keep_going = false;
				continue;
			}
			int num2 = Convert.ToInt32((num >> 48) & 0xFF);
			if (num2 < 1)
			{
				keep_going = false;
				continue;
			}
			LoadedData.AddDataFromRaw(num, this, adjctrlDCO0, adjctrlDCO1, new_cal, TA_C, TOT_C);
			n_read++;
			if (n_read > threshold)
			{
				keep_going = false;
			}
		}
		return n_read;
	}

	private void Ignite32_TOP_Write_Single(int intAddr, int writeVal, int size, int pos)
	{
		if (size > 8 || pos > 7 || writeVal > 255)
		{
			MessageBox.Show("One value out of range in \n Ignite32_TOP_Write_Single");
			return;
		}
		int value = Convert.ToInt32(TOP_I2C_addr_tBox.Text, 16);
		byte b;
		byte ReadData;
		if (!debug)
		{
			b = I2C_Guarded_ReadByte(Convert.ToByte(value), Convert.ToByte(intAddr), out ReadData);
		}
		else
		{
			ReadData = 0;
			b = 0;
		}
		int num = Convert.ToInt32(ReadData);
		int num2 = (1 << size) - 1 << pos;
		int num3 = num & ~num2;
		int num4 = (writeVal << pos) & num2;
		int value2 = num3 | num4;
		if (!debug)
		{
			b = I2C_Guarded_WriteByte(Convert.ToByte(value), Convert.ToByte(intAddr), Convert.ToByte(value2));
		}
		if (b != 0)
		{
			MessageBox.Show(b.ToString());
		}
	}

	private void Ignite32_MAT_Write_Single(int MATid, int intAddr, int writeVal, int size, int pos)
	{
		if (size > 8 || pos > 7 || writeVal > 255)
		{
			MessageBox.Show("One value out of range in \n Ignite32_MAT_Write_Single");
			return;
		}
		if (MATid > 15)
		{
			MessageBox.Show("MATid > 15 \n Ignite32_MAT_Write_Single");
			return;
		}
		int value = Convert.ToByte(Ignite32_MATID_ToIntDevAddr(MATid));
		byte b;
		byte ReadData;
		if (!debug)
		{
			b = I2C_Guarded_ReadByte(Convert.ToByte(value), Convert.ToByte(intAddr), out ReadData);
		}
		else
		{
			ReadData = 0;
			b = 0;
		}
		int num = Convert.ToInt32(ReadData);
		int num2 = (1 << size) - 1 << pos;
		int num3 = num & ~num2;
		int num4 = (writeVal << pos) & num2;
		int value2 = num3 | num4;
		if (!debug)
		{
			b = I2C_Guarded_WriteByte(Convert.ToByte(value), Convert.ToByte(intAddr), Convert.ToByte(value2));
		}
		if (b != 0)
		{
			MessageBox.Show(b.ToString());
		}
	}

	private void Ignite32_IOext_Write_Single(int intAddr, int writeVal, int size, int pos)
	{
		if (size > 8 || pos > 7 || writeVal > 255)
		{
			MessageBox.Show("One value out of range in \n Ignite32_IOext_Write_Single");
			return;
		}
		int value = 64;
		byte b;
		byte ReadData;
		if (!debug)
		{
			b = I2C_Guarded_ReadByte(Convert.ToByte(value), Convert.ToByte(intAddr), out ReadData);
		}
		else
		{
			ReadData = 0;
			b = 0;
		}
		int num = Convert.ToInt32(ReadData);
		int num2 = (1 << size) - 1 << pos;
		int num3 = num & ~num2;
		int num4 = (writeVal << pos) & num2;
		int value2 = num3 | num4;
		if (!debug)
		{
			b = I2C_Guarded_WriteByte(Convert.ToByte(value), Convert.ToByte(intAddr), Convert.ToByte(value2));
		}
		if (b != 0)
		{
			MessageBox.Show(b.ToString());
		}
	}

	private int Ignite32_MATID_ToIntDevAddr(int MatID)
	{
		int num = 0;
		if (MatID > 15)
		{
			return 254;
		}
		return 2 * Math.Abs(MatID);
	}

	private int Ignite32_PIXStatus_ToIntWbyte(bool FE_ON = false, bool PIXON = true, int adj = 0, int ctrl = 0)
	{
		if (adj > 3 || ctrl > 15)
		{
			throw new ArgumentOutOfRangeException("adj", "Adj or Ctrl out of range");
		}
		return Convert.ToInt32(FE_ON) * 128 + Convert.ToInt32(PIXON) * 64 + adj * 16 + ctrl;
	}

	private void Ignite32_Mat_TDC_DCO0conf_noUI(int MatID, bool? DE_ON = null, bool? TDCON = null, int? adj = null, int? ctrl = null)
	{
		if (adj.HasValue && (adj < 0 || adj > 3))
		{
			throw new ArgumentOutOfRangeException("adj", "Adj out of range");
		}
		if (ctrl.HasValue && (ctrl < 0 || ctrl > 15))
		{
			throw new ArgumentOutOfRangeException("ctrl", "Ctrl out of range");
		}
		byte address = Convert.ToByte(Ignite32_MATID_ToIntDevAddr(MatID));
		byte b = ((MatID <= 15) ? I2C_Guarded_ReadByte(address, Convert.ToByte(64), out var ReadData) : I2C_Guarded_ReadByte(Convert.ToByte(0), Convert.ToByte(64), out ReadData));
		if (b != 0)
		{
			MessageBox.Show(b.ToString());
			return;
		}
		int num = 0;
		bool value = DE_ON ?? ((ReadData & 0x80) != 0);
		bool value2 = TDCON ?? ((ReadData & 0x40) != 0);
		int num2 = adj ?? ((ReadData >> 4) & 3);
		int num3 = ctrl ?? (ReadData & 0xF);
		num = Convert.ToInt32(value) * 128 + Convert.ToInt32(value2) * 64 + num2 * 16 + num3;
		byte dataByte = Convert.ToByte(num);
		b = I2C_Guarded_WriteByte(address, Convert.ToByte(64), dataByte);
		if (b != 0)
		{
			MessageBox.Show(b.ToString());
		}
	}

	private void Ignite32_Mat_PIX_conf_noUI(int MatID, int PixID, bool? FE_ON = null, bool? PIXON = null, int? adj = null, int? ctrl = null)
	{
		if (adj.HasValue && (adj < 0 || adj > 3))
		{
			Console.WriteLine("Dio Can ");
			throw new ArgumentOutOfRangeException("adj", "Adj out of range");
		}
		if (ctrl.HasValue && (ctrl < 0 || ctrl > 15))
		{
			throw new ArgumentOutOfRangeException("ctrl", "Ctrl out of range");
		}
		byte address = Convert.ToByte(Ignite32_MATID_ToIntDevAddr(MatID));
		byte b = ((MatID <= 15) ? I2C_Guarded_ReadByte(address, Convert.ToByte(PixID), out var ReadData) : I2C_Guarded_ReadByte(Convert.ToByte(0), Convert.ToByte(PixID), out ReadData));
		if (b != 0)
		{
			MessageBox.Show(b.ToString());
			return;
		}
		int num = 0;
		bool value = FE_ON ?? ((ReadData & 0x80) != 0);
		bool value2 = PIXON ?? ((ReadData & 0x40) != 0);
		int num2 = adj ?? ((ReadData >> 4) & 3);
		int num3 = ctrl ?? (ReadData & 0xF);
		num = Convert.ToInt32(value) * 128 + Convert.ToInt32(value2) * 64 + num2 * 16 + num3;
		byte dataByte = Convert.ToByte(num);
		b = I2C_Guarded_WriteByte(address, Convert.ToByte(PixID), dataByte);
		if (b != 0)
		{
			MessageBox.Show(b.ToString());
		}
	}

	private void Ignite32_Mat_allPIX_conf_noUI(int MatID, bool? FE_ON = null, bool? PIXON = null, int? adj = null, int? ctrl = null)
	{
		if (adj.HasValue && (adj < 0 || adj > 3))
		{
			throw new ArgumentOutOfRangeException("adj", "Adj out of range");
		}
		if (ctrl.HasValue && (ctrl < 0 || ctrl > 15))
		{
			throw new ArgumentOutOfRangeException("ctrl", "Ctrl out of range");
		}
		for (int i = 0; i < 64; i++)
		{
			Ignite32_Mat_PIX_conf_noUI(MatID, i, FE_ON, PIXON, adj, ctrl);
		}
	}

	private void Ignite32_Mat_CAL_conf_noUI(int MatID, bool? EN_CON_PAD = null, bool? EN_P_VTH = null, bool? EN_P_Vldo = null, bool? EN_P_VFB = null, bool? EN_TimeOut = null, bool? CalMode = null, int? CalTime = null)
	{
		if (CalTime.HasValue && (CalTime < 0 || CalTime > 3))
		{
			throw new ArgumentOutOfRangeException("CalTime", "CalTime out of range");
		}
		byte address = Convert.ToByte(Ignite32_MATID_ToIntDevAddr(MatID));
		byte b = ((MatID <= 15) ? I2C_Guarded_ReadByte(address, Convert.ToByte(67), out var ReadData) : I2C_Guarded_ReadByte(Convert.ToByte(0), Convert.ToByte(67), out ReadData));
		if (b != 0)
		{
			MessageBox.Show(b.ToString());
			return;
		}
		int num = 0;
		bool value = EN_CON_PAD ?? ((ReadData & 0x80) != 0);
		bool value2 = EN_P_VTH ?? ((ReadData & 0x40) != 0);
		bool value3 = EN_P_Vldo ?? ((ReadData & 0x20) != 0);
		bool value4 = EN_P_VFB ?? ((ReadData & 0x10) != 0);
		bool value5 = EN_TimeOut ?? ((ReadData & 8) != 0);
		bool value6 = CalMode ?? ((ReadData & 4) != 0);
		int num2 = CalTime ?? (ReadData & 3);
		num = Convert.ToInt32(value) * 128 + Convert.ToInt32(value2) * 64 + Convert.ToInt32(value3) * 32 + Convert.ToInt32(value4) * 16 + Convert.ToInt32(value5) * 8 + Convert.ToInt32(value6) * 4 + num2;
		byte dataByte = Convert.ToByte(num);
		b = I2C_Guarded_WriteByte(address, Convert.ToByte(67), dataByte);
		if (b != 0)
		{
			MessageBox.Show(b.ToString());
		}
	}

	private void Ignite32_Mat_Command_noUI(int MatID, int CalSelDCO, bool? DAQ_RES = null, bool? group48_63 = null, bool? group32_47 = null, bool? group16_31 = null, bool? group00_15 = null)
	{
		if (CalSelDCO < 0 || CalSelDCO > 1)
		{
			throw new ArgumentOutOfRangeException("CalSelDCO", "CalSelDCO out of range");
		}
		byte address = Convert.ToByte(Ignite32_MATID_ToIntDevAddr(MatID));
		int num = 0;
		bool valueOrDefault = DAQ_RES == true;
		bool valueOrDefault2 = group48_63 == true;
		bool valueOrDefault3 = group32_47 == true;
		bool valueOrDefault4 = group16_31 == true;
		bool valueOrDefault5 = group00_15 == true;
		num = Convert.ToInt32(valueOrDefault) * 128 + CalSelDCO * 16 + Convert.ToInt32(valueOrDefault2) * 8 + Convert.ToInt32(valueOrDefault3) * 4 + Convert.ToInt32(valueOrDefault4) * 2 + Convert.ToInt32(valueOrDefault5);
		byte dataByte = Convert.ToByte(num);
		byte b = I2C_Guarded_WriteByte(address, Convert.ToByte(112), dataByte);
		if (b != 0)
		{
			MessageBox.Show(b.ToString());
		}
	}

	private int Ignite32_DCO_conf_read(int MatID, int DCO, int? PIX = null)
	{
		int result = 0;
		byte address = Convert.ToByte(Ignite32_MATID_ToIntDevAddr(MatID));
		byte ReadData;
		if (DCO == 0)
		{
			byte b = ((MatID <= 15) ? I2C_Guarded_ReadByte(address, Convert.ToByte(64), out ReadData) : I2C_Guarded_ReadByte(Convert.ToByte(0), Convert.ToByte(64), out ReadData));
			if (b != 0)
			{
				MessageBox.Show(b.ToString());
				return 0;
			}
			result = ReadData & 0x3F;
		}
		if (DCO == 1)
		{
			byte b = ((MatID <= 15) ? I2C_Guarded_ReadByte(address, Convert.ToByte(PIX), out ReadData) : I2C_Guarded_ReadByte(Convert.ToByte(0), Convert.ToByte(PIX), out ReadData));
			if (b != 0)
			{
				MessageBox.Show(b.ToString());
				return 0;
			}
			result = ReadData & 0x3F;
		}
		return result;
	}

	private async void Write_LastNdata_ToFile(string filename, int N_Data = int.MaxValue, int TA_code = 999, int TOT_code = 999, int Quad = 999)
	{
		int[] TextColumnWidths = new int[25]
		{
			16, 11, 8, 3, 3, 3, 5, 10, 3, 2,
			8, 5, 5, 7, 9, 9, 10, 10, 10, 10,
			10, 10, 10, 10, 10
		};
		List<DataEntry> entries = LoadedData.GetAllEntriesInOrder();
		using StreamWriter writer = new StreamWriter(filename, append: true);
		int num = entries.Count - N_Data;
		if (N_Data == int.MaxValue)
		{
			num = 0;
		}
		for (int i = num; i < entries.Count; i++)
		{
			DataEntry dataEntry = entries[i];
			ulong dataFifoRaw = Convert.ToUInt64(dataEntry.RAW, 16);
			object[] array = RawDataToObjArray(dataFifoRaw);
			int num2 = array.Length;
			bool flag = Convert.ToBoolean(array[6]);
			int num3 = Convert.ToInt32(array[3]);
			int num4 = Convert.ToInt32(array[4]);
			List<object> DataToPrint = new List<object>();
			for (int j = 0; j < num2; j++)
			{
				DataToPrint.Add(array[j]);
			}
			DataToPrint.Add(dataEntry.AdjCtrl_DCO0);
			DataToPrint.Add(dataEntry.AdjCtrl_DCO1);
			if (Quad != 999)
			{
				DataToPrint.Add(Quad);
			}
			else
			{
				DataToPrint.Add("-");
			}
			string item = (dataEntry.TOT_Code.HasValue ? Math.Round(1562.5 * ((double)(int)dataEntry.TOT_Code.Value + 2.0), 2).ToString() : "-");
			string item2 = (dataEntry.TA_Code.HasValue ? Math.Round(25000.0 - 1562.5 * (double)(int)dataEntry.TA_Code.Value, 2).ToString() : "-");
			if (!flag)
			{
				DataToPrint.Add(Math.Round(LoadedData.CAL_Matrix[Cur_Quad, num3, num4, 0], 2));
				DataToPrint.Add(Math.Round(LoadedData.CAL_Matrix[Cur_Quad, num3, num4, 1], 2));
				DataToPrint.Add(item2);
				DataToPrint.Add(Math.Round(dataEntry.TA_picoS.Value));
				DataToPrint.Add(item);
				DataToPrint.Add(Math.Round(dataEntry.TOT_picoS.Value));
				ushort? tA_Code = dataEntry.TA_Code;
				DataToPrint.Add(tA_Code.HasValue ? ((object)tA_Code.GetValueOrDefault()) : "-");
				tA_Code = dataEntry.TOT_Code;
				DataToPrint.Add(tA_Code.HasValue ? ((object)tA_Code.GetValueOrDefault()) : "-");
			}
			else
			{
				bool flag2 = Convert.ToBoolean(array[8]);
				DataToPrint.Add(flag2 ? "-" : Math.Round(dataEntry.DCO0_T_picoS.Value, 2).ToString());
				DataToPrint.Add(flag2 ? Math.Round(dataEntry.DCO1_T_picoS.Value, 2).ToString() : "-");
				DataToPrint.Add("-");
				DataToPrint.Add("-");
				DataToPrint.Add("-");
				DataToPrint.Add("-");
				DataToPrint.Add("-");
				DataToPrint.Add("-");
			}
			for (int k = 0; k < DataToPrint.Count; k++)
			{
				await writer.WriteAsync(DataToPrint[k].ToString().PadRight(TextColumnWidths[k] + 4) + "\t");
			}
			await writer.WriteLineAsync();
		}
	}

	public async Task<string> Write_LastNdata_ToString(int N_Data = int.MaxValue, int sTA_code = 999, int sTOT_code = 999, int Quad = 999)
	{
		return await Task.Run(delegate
		{
			int[] array = new int[25]
			{
				16, 11, 8, 3, 3, 3, 5, 10, 3, 2,
				8, 5, 5, 7, 9, 9, 10, 10, 10, 10,
				10, 10, 10, 10, 10
			};
			List<DataEntry> allEntriesInOrder = LoadedData.GetAllEntriesInOrder();
			StringBuilder stringBuilder = new StringBuilder();
			int num = allEntriesInOrder.Count - N_Data;
			if (N_Data == int.MaxValue)
			{
				num = 0;
			}
			for (int i = num; i < allEntriesInOrder.Count; i++)
			{
				DataEntry dataEntry = allEntriesInOrder[i];
				ulong dataFifoRaw = Convert.ToUInt64(dataEntry.RAW, 16);
				object[] array2 = RawDataToObjArray(dataFifoRaw);
				int num2 = array2.Length;
				bool flag = Convert.ToBoolean(array2[6]);
				int num3 = Convert.ToInt32(array2[3]);
				int num4 = Convert.ToInt32(array2[4]);
				List<object> list = new List<object>();
				for (int j = 0; j < num2; j++)
				{
					list.Add(array2[j]);
				}
				ushort? adjCtrl_DCO = dataEntry.AdjCtrl_DCO0;
				list.Add(adjCtrl_DCO.HasValue ? ((object)adjCtrl_DCO.GetValueOrDefault()) : "-");
				adjCtrl_DCO = dataEntry.AdjCtrl_DCO1;
				list.Add(adjCtrl_DCO.HasValue ? ((object)adjCtrl_DCO.GetValueOrDefault()) : "-");
				if (Quad != 999)
				{
					list.Add(Quad);
				}
				else
				{
					list.Add("-");
				}
				string item = (dataEntry.TOT_Code.HasValue ? Math.Round(1562.5 * ((double)(int)dataEntry.TOT_Code.Value + 2.0), 2).ToString() : "-");
				string item2 = (dataEntry.TA_Code.HasValue ? Math.Round(25000.0 - 1562.5 * (double)(int)dataEntry.TA_Code.Value, 2).ToString() : "-");
				if (!flag)
				{
					list.Add(Math.Round(LoadedData.CAL_Matrix[Cur_Quad, num3, num4, 0], 2));
					list.Add(Math.Round(LoadedData.CAL_Matrix[Cur_Quad, num3, num4, 1], 2));
					list.Add(item2);
					list.Add(Math.Round(dataEntry.TA_picoS.Value));
					list.Add(item);
					list.Add(Math.Round(dataEntry.TOT_picoS.Value));
					adjCtrl_DCO = dataEntry.TA_Code;
					list.Add(adjCtrl_DCO.HasValue ? ((object)adjCtrl_DCO.GetValueOrDefault()) : "-");
					adjCtrl_DCO = dataEntry.TOT_Code;
					list.Add(adjCtrl_DCO.HasValue ? ((object)adjCtrl_DCO.GetValueOrDefault()) : "-");
				}
				else
				{
					bool flag2 = Convert.ToBoolean(array2[8]);
					list.Add(flag2 ? Math.Round(LoadedData.CAL_Matrix[Cur_Quad, num3, num4, 0], 2).ToString() : "-");
					list.Add(flag2 ? "-" : Math.Round(LoadedData.CAL_Matrix[Cur_Quad, num3, num4, 1], 2).ToString());
					list.Add("-");
					list.Add("-");
					list.Add("-");
					list.Add("-");
					list.Add("-");
					list.Add("-");
				}
				for (int k = 0; k < list.Count; k++)
				{
					stringBuilder.Append(list[k].ToString().PadRight(array[k] + 4) + "\t");
				}
				stringBuilder.AppendLine();
			}
			return stringBuilder.ToString();
		});
	}

	public List<double> RemoveOutliers(List<double> data, double zThreshold = 3.0)
	{
		double mean = Statistics.Mean((IEnumerable<double>)data);
		double stdDev = Statistics.StandardDeviation((IEnumerable<double>)data);
		if (double.IsNaN(stdDev) || stdDev == 0.0)
		{
			return new List<double>(data);
		}
		return data.Where((double x) => Math.Abs((x - mean) / stdDev) <= zThreshold).ToList();
	}

	public List<double> RemoveOutliersUsingMAD(List<double> data, double madThresholdFactor = 3.0)
	{
		List<double> list = new List<double>(data);
		for (int i = 0; i < 1; i++)
		{
			double median = Statistics.Median((IEnumerable<double>)list);
			List<double> list2 = list.Select((double x) => Math.Abs(x - median)).ToList();
			double mad = Statistics.Median((IEnumerable<double>)list2);
			List<double> list3 = list.Where((double x) => Math.Abs(x - median) > madThresholdFactor * mad).ToList();
			if (!list3.Any())
			{
				break;
			}
			list = list.Except(list3).ToList();
		}
		return list;
	}

	public static List<double> ChauvenetFilter(List<double> data, double Threshold = 4.0)
	{
		if (data == null)
		{
			throw new ArgumentNullException("data");
		}
		if (data.Count < 2)
		{
			return new List<double>(data);
		}
		int count = data.Count;
		double mean = data.Average();
		double d = data.Sum((double x) => (x - mean) * (x - mean)) / (double)(count - 1);
		double num = Math.Sqrt(d);
		if (num == 0.0)
		{
			return new List<double>(data);
		}
		double num2 = 1.0 / (4.0 * (double)count);
		double val = Normal.InvCDF(0.0, 1.0, 1.0 - num2);
		double num3 = Math.Max(val, Math.Abs(Threshold));
		double thresh = num3 * num;
		return data.Where((double x) => Math.Abs(x - mean) <= thresh).ToList();
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
		System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle = new System.Windows.Forms.DataGridViewCellStyle();
		System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
		System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
		System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
		System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(tb_Ignite64.MainForm));
		System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle5 = new System.Windows.Forms.DataGridViewCellStyle();
		System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle6 = new System.Windows.Forms.DataGridViewCellStyle();
		this.TDCcalibAll_contMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
		this.calibALLTDCToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.calibCol0TDCsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.calibCol1TDCsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.TDCwriteAll_contMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
		this.writeALLTDCsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.writeTDCsCol0ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.writeTDCsCol1ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.menuStrip1 = new System.Windows.Forms.MenuStrip();
		this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.loadConfigToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.saveConfigToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.toolStripMenuItem6 = new System.Windows.Forms.ToolStripSeparator();
		this.saveLogToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.clearLogToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripSeparator();
		this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.i2cToolsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.infoToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.selectDevicesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.SelDevToolStripComboBox = new System.Windows.Forms.ToolStripComboBox();
		this.setI2CFrequencyToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.kHz100ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.kHz500ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.mHzToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.BusRecoveryToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.toolStripMenuItem4 = new System.Windows.Forms.ToolStripSeparator();
		this.scanI2CBusToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
		this.debugModeToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.sI5341ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.runWizardToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.loadConfigFileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.globalsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.resetToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.toolStripMenuItem2 = new System.Windows.Forms.ToolStripSeparator();
		this.initializingToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.externalDACsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.internalDACsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.toolStripMenuItem3 = new System.Windows.Forms.ToolStripSeparator();
		this.rowToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.tDCToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.TDCautoTestToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.dCOTestToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.tDCTestToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.tDCDataRawToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.plotResultsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.plotResultsToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
		this.infoToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
		this.ignite0LayoutMapToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.viewDatasheetToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.ignite32ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.pCBSchemesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.aDCToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.dACToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.iOExpanderToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
		this.statusStrip1 = new System.Windows.Forms.StatusStrip();
		this.BuiltData_tSSLabel = new System.Windows.Forms.ToolStripStatusLabel();
		this.HW_detect_tSSLabel = new System.Windows.Forms.ToolStripStatusLabel();
		this.I2C_freq_tSSLabel = new System.Windows.Forms.ToolStripStatusLabel();
		this.DevSerial_tSSLabel = new System.Windows.Forms.ToolStripStatusLabel();
		this.AnalogPowerStatus_tSSLabel = new System.Windows.Forms.ToolStripStatusLabel();
		this.MuxStatus_tSSLabel = new System.Windows.Forms.ToolStripStatusLabel();
		this.I2C_oper_tSSLabel = new System.Windows.Forms.ToolStripStatusLabel();
		this.Log_textBox = new System.Windows.Forms.TextBox();
		this.saveLogFileDialog = new System.Windows.Forms.SaveFileDialog();
		this.saveFileDialog1 = new System.Windows.Forms.SaveFileDialog();
		this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
		this.SEL_CAL_TIME_UpDown = new System.Windows.Forms.NumericUpDown();
		this.TDC_ON_chkBox = new System.Windows.Forms.CheckBox();
		this.EN_TIMEOUT_chkBox = new System.Windows.Forms.CheckBox();
		this.enDEtot_chkBox = new System.Windows.Forms.CheckBox();
		this.CAL_MODE_chkBox = new System.Windows.Forms.CheckBox();
		this.PIX_Sel_UpDown = new System.Windows.Forms.NumericUpDown();
		this.PIX_ON_ALL_chkBox = new System.Windows.Forms.CheckBox();
		this.DCO0ctrl_UpDown = new System.Windows.Forms.NumericUpDown();
		this.DCO0adj_UpDown = new System.Windows.Forms.NumericUpDown();
		this.DCO_PIX_adj_UpDown = new System.Windows.Forms.NumericUpDown();
		this.DCO_PIX_ctrl_UpDown = new System.Windows.Forms.NumericUpDown();
		this.PIX_DCO_ALL_chkBox = new System.Windows.Forms.CheckBox();
		this.MAT_FE_ALL_chkBox = new System.Windows.Forms.CheckBox();
		this.MAT_FE_ON_chkBox = new System.Windows.Forms.CheckBox();
		this.PIX_ON_chkBox = new System.Windows.Forms.CheckBox();
		this.CAL_SEL_DCO_comboBox = new System.Windows.Forms.ComboBox();
		this.MAT_DAC_VTH_H_EN_chkBox = new System.Windows.Forms.CheckBox();
		this.MAT_DAC_FT_UpDown = new System.Windows.Forms.NumericUpDown();
		this.MAT_DAC_VTH_H_UpDown = new System.Windows.Forms.NumericUpDown();
		this.MAT_DAC_ALL_FT_chkBox = new System.Windows.Forms.CheckBox();
		this.MAT_DAC_FT_SEL_UpDown = new System.Windows.Forms.NumericUpDown();
		this.SEL_VINJ_MUX_High_comboBox = new System.Windows.Forms.ComboBox();
		this.MAT_DAC_VTH_L_UpDown = new System.Windows.Forms.NumericUpDown();
		this.MAT_DAC_VTH_L_EN_chkBox = new System.Windows.Forms.CheckBox();
		this.MAT_DAC_VINJ_H_UpDown = new System.Windows.Forms.NumericUpDown();
		this.MAT_DAC_VINJ_H_EN_chkBox = new System.Windows.Forms.CheckBox();
		this.MAT_DAC_VINJ_L_UpDown = new System.Windows.Forms.NumericUpDown();
		this.MAT_DAC_VINJ_L_EN_chkBox = new System.Windows.Forms.CheckBox();
		this.MAT_DAC_VLDO_UpDown = new System.Windows.Forms.NumericUpDown();
		this.MAT_DAC_VLDO_EN_chkBox = new System.Windows.Forms.CheckBox();
		this.MAT_DAC_VFB_UpDown = new System.Windows.Forms.NumericUpDown();
		this.MAT_DAC_VFB_EN_chkBox = new System.Windows.Forms.CheckBox();
		this.DAC_IDISC_UpDown = new System.Windows.Forms.NumericUpDown();
		this.DAC_ICSA_UpDown = new System.Windows.Forms.NumericUpDown();
		this.DAC_IKRUM_UpDown = new System.Windows.Forms.NumericUpDown();
		this.CH_MODE_42_comboBox = new System.Windows.Forms.ComboBox();
		this.CH_MODE_41_comboBox = new System.Windows.Forms.ComboBox();
		this.CH_SEL_42_UpDown = new System.Windows.Forms.NumericUpDown();
		this.CH_SEL_41_UpDown = new System.Windows.Forms.NumericUpDown();
		this.SEL_VINJ_MUX_Low_comboBox = new System.Windows.Forms.ComboBox();
		this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
		this.exAdcDac_tabPage = new System.Windows.Forms.TabPage();
		this.Quad_DAC_grpBox = new System.Windows.Forms.GroupBox();
		this.Ext_Icap_label = new System.Windows.Forms.Label();
		this.DACicap_UpDown = new System.Windows.Forms.NumericUpDown();
		this.DACicap_but = new System.Windows.Forms.Button();
		this.Ext_Iref_label = new System.Windows.Forms.Label();
		this.DACiref_UpDown = new System.Windows.Forms.NumericUpDown();
		this.DACiref_but = new System.Windows.Forms.Button();
		this.Ext_Vfeed_label = new System.Windows.Forms.Label();
		this.textBox1 = new System.Windows.Forms.TextBox();
		this.DACvfeed_UpDown = new System.Windows.Forms.NumericUpDown();
		this.DACvfeed_but = new System.Windows.Forms.Button();
		this.Ext_Quad_VDDA_label = new System.Windows.Forms.Label();
		this.groupBox1 = new System.Windows.Forms.GroupBox();
		this.groupBox2 = new System.Windows.Forms.GroupBox();
		this.Ext_ThresholdScan_label = new System.Windows.Forms.Label();
		this.ScanVthStep_UpDown = new System.Windows.Forms.NumericUpDown();
		this.ScanVthMin_UpDown = new System.Windows.Forms.NumericUpDown();
		this.ScanVthMax_UpDown = new System.Windows.Forms.NumericUpDown();
		this.groupBox4 = new System.Windows.Forms.GroupBox();
		this.Ext_Vinj2Scan_label = new System.Windows.Forms.Label();
		this.ScanVinj2Step_UpDown = new System.Windows.Forms.NumericUpDown();
		this.ScanVinj2Min_UpDown = new System.Windows.Forms.NumericUpDown();
		this.ScanVinj2Max_UpDown = new System.Windows.Forms.NumericUpDown();
		this.ExtSelQuad_SE_chkBox = new System.Windows.Forms.CheckBox();
		this.ExtSelQuad_NE_chkBox = new System.Windows.Forms.CheckBox();
		this.ExtSelQuad_SW_chkBox = new System.Windows.Forms.CheckBox();
		this.ExtSelQuad_NW_chkBox = new System.Windows.Forms.CheckBox();
		this.Quad_ADC_grpBox = new System.Windows.Forms.GroupBox();
		this.ADCdac_Quad_NShot_label = new System.Windows.Forms.Label();
		this.ADCdac_Quad_NShot_button = new System.Windows.Forms.Button();
		this.ADCdac_Quad_NShot_numUpDown = new System.Windows.Forms.NumericUpDown();
		this.ADCdac_Quad_Value_tBox = new System.Windows.Forms.TextBox();
		this.Ext_QuadADC_Config_label = new System.Windows.Forms.Label();
		this.ADCdac_Quad_Config_tBox = new System.Windows.Forms.TextBox();
		this.ADCdac_Quad_DataDec_tBox = new System.Windows.Forms.TextBox();
		this.Ext_QuadADC_Data_label = new System.Windows.Forms.Label();
		this.ADCdac_Quad_DataHex_tBox = new System.Windows.Forms.TextBox();
		this.ADCdac_Quad_Read_but = new System.Windows.Forms.Button();
		this.ADCdac_Quad_1Shot_but = new System.Windows.Forms.Button();
		this.ADCdac_Quad_Write_but = new System.Windows.Forms.Button();
		this.Ext_QuadADC_ResGain_label = new System.Windows.Forms.Label();
		this.Ext_QuadADC_Val_Monitored_label = new System.Windows.Forms.Label();
		this.ADCdac_Quad_Gain_comboBox = new System.Windows.Forms.ComboBox();
		this.ADCdac_Quad_Res_comboBox = new System.Windows.Forms.ComboBox();
		this.ADCdac_Quad_ch_comboBox = new System.Windows.Forms.ComboBox();
		this.ADCdac_Quad_RDY_chkBox = new System.Windows.Forms.CheckBox();
		this.ADCdac_Quad_OC_chkBox = new System.Windows.Forms.CheckBox();
		this.Ext_QuadADC_HexDecV_label = new System.Windows.Forms.Label();
		this.CurMonADC_groupBox = new System.Windows.Forms.GroupBox();
		this.ADCcommon_NShot_label = new System.Windows.Forms.Label();
		this.ADCcommon_NShot_button = new System.Windows.Forms.Button();
		this.ADCcommon_NShot_numUpDown = new System.Windows.Forms.NumericUpDown();
		this.ADCcommon_Value_tBox = new System.Windows.Forms.TextBox();
		this.Ext_CommonADC_Config_label = new System.Windows.Forms.Label();
		this.ADCcommon_Config_tBox = new System.Windows.Forms.TextBox();
		this.ADCcommon_DataDec_tBox = new System.Windows.Forms.TextBox();
		this.Ext_CommonADC_Data_label = new System.Windows.Forms.Label();
		this.ADCcommon_DataHex_tBox = new System.Windows.Forms.TextBox();
		this.ADCcommon_Read_but = new System.Windows.Forms.Button();
		this.ADCcommon_1Shot_but = new System.Windows.Forms.Button();
		this.ADCcommon_Write_but = new System.Windows.Forms.Button();
		this.Ext_CommonADC_ResGain_label = new System.Windows.Forms.Label();
		this.Ext_CommonADC_Val_Monitored_label = new System.Windows.Forms.Label();
		this.ADCcommon_Gain_comboBox = new System.Windows.Forms.ComboBox();
		this.ADCcommon_Res_comboBox = new System.Windows.Forms.ComboBox();
		this.ADCcommon_ch_comboBox = new System.Windows.Forms.ComboBox();
		this.ADCcommon_RDY_chkBox = new System.Windows.Forms.CheckBox();
		this.ADCcommon_OC_chkBox = new System.Windows.Forms.CheckBox();
		this.Ext_CommonADC_HexDecV_label = new System.Windows.Forms.Label();
		this.ExtDAC_groupBox = new System.Windows.Forms.GroupBox();
		this.Ext_Common_Vref_label = new System.Windows.Forms.Label();
		this.DACvref_L_UpDown = new System.Windows.Forms.NumericUpDown();
		this.DACvref_L_but = new System.Windows.Forms.Button();
		this.DACVext_but = new System.Windows.Forms.Button();
		this.VDDA_txtBox = new System.Windows.Forms.TextBox();
		this.Ext_Common_VDDA_label = new System.Windows.Forms.Label();
		this.DACvthr_H_UpDown = new System.Windows.Forms.NumericUpDown();
		this.DACvinj_H_but = new System.Windows.Forms.Button();
		this.DACvthr_L_UpDown = new System.Windows.Forms.NumericUpDown();
		this.DACvthr_L_but = new System.Windows.Forms.Button();
		this.DACvinj_H_UpDown = new System.Windows.Forms.NumericUpDown();
		this.DACvthr_H_but = new System.Windows.Forms.Button();
		this.DACVext_UpDown = new System.Windows.Forms.NumericUpDown();
		this.Ext_Common_VthVinjVext_label = new System.Windows.Forms.Label();
		this.Ctrl_tabPage = new System.Windows.Forms.TabPage();
		this.groupBox3 = new System.Windows.Forms.GroupBox();
		this.AllMAT_AllAFE_OFF_but = new System.Windows.Forms.Button();
		this.AllMAT_AllAFE_ON_but = new System.Windows.Forms.Button();
		this.AllMAT_AllTDC_OFF_but = new System.Windows.Forms.Button();
		this.AllMAT_AllTDC_ON_but = new System.Windows.Forms.Button();
		this.AllMAT_AllPIX_OFF_but = new System.Windows.Forms.Button();
		this.AllMAT_AllPIX_ON_but = new System.Windows.Forms.Button();
		this.Mat_tabPage = new System.Windows.Forms.TabPage();
		this.MAT_Test_Routines_groupBox = new System.Windows.Forms.GroupBox();
		this.TestATP_but = new System.Windows.Forms.Button();
		this.CalDCO_save_but = new System.Windows.Forms.Button();
		this.MAT_DAC_VLDO_tmp_but = new System.Windows.Forms.Button();
		this.CalibDCO_but = new System.Windows.Forms.Button();
		this.TestDCO_but = new System.Windows.Forms.Button();
		this.TestTDC_but = new System.Windows.Forms.Button();
		this.Visualize_DCOCalibration_but = new System.Windows.Forms.Button();
		this.MatSelQuad_SE_chkBox = new System.Windows.Forms.CheckBox();
		this.MatSelQuad_NE_chkBox = new System.Windows.Forms.CheckBox();
		this.MatSelQuad_SW_chkBox = new System.Windows.Forms.CheckBox();
		this.MatSelQuad_NW_chkBox = new System.Windows.Forms.CheckBox();
		this.MAT_AFE_groupBox = new System.Windows.Forms.GroupBox();
		this.label1 = new System.Windows.Forms.Label();
		this.AFE_MAT_Sel_comboBox = new System.Windows.Forms.ComboBox();
		this.SEL_VINJ_MUX_Low_label = new System.Windows.Forms.Label();
		this.EN_P_VTH_chkBox = new System.Windows.Forms.CheckBox();
		this.EXT_DC_chkBox = new System.Windows.Forms.CheckBox();
		this.AFE_LB_chkBox = new System.Windows.Forms.CheckBox();
		this.AFE_AUTO_chkBox = new System.Windows.Forms.CheckBox();
		this.EN_P_VLDO_chkBox = new System.Windows.Forms.CheckBox();
		this.CON_PAD_chkBox = new System.Windows.Forms.CheckBox();
		this.EN_P_VFB_chkBox = new System.Windows.Forms.CheckBox();
		this.EN_P_VINJ_chkBox = new System.Windows.Forms.CheckBox();
		this.SEL_VINJ_MUX_High_label = new System.Windows.Forms.Label();
		this.PIX_groupBox = new System.Windows.Forms.GroupBox();
		this.MAT_DCO0_Period_textBox = new System.Windows.Forms.TextBox();
		this.MAT_DCO1_Period_textBox = new System.Windows.Forms.TextBox();
		this.MAT_DCO_Difference_textBox = new System.Windows.Forms.TextBox();
		this.MAT_DCO_Period_label = new System.Windows.Forms.Label();
		this.PIX_DCO_label = new System.Windows.Forms.Label();
		this.adj_ctrl_1_label = new System.Windows.Forms.Label();
		this.PIX_Sel_label = new System.Windows.Forms.Label();
		this.MAT_DAC_groupBox = new System.Windows.Forms.GroupBox();
		this.DAC_Ikrum_label = new System.Windows.Forms.Label();
		this.DAC_ICSA_label = new System.Windows.Forms.Label();
		this.DAC_IDISC_label = new System.Windows.Forms.Label();
		this.MAT_DAC_FT_label = new System.Windows.Forms.Label();
		this.MAT_DAC_FT_SEL_label = new System.Windows.Forms.Label();
		this.MAT_cfg_groupBox = new System.Windows.Forms.GroupBox();
		this.CH_MODE_label = new System.Windows.Forms.Label();
		this.CAL_TIME_label = new System.Windows.Forms.Label();
		this.DCO0_MatTab_label = new System.Windows.Forms.Label();
		this.DCO0_adj_ctr_MatTab_label = new System.Windows.Forms.Label();
		this.MAT_COMMANDS_groupBox = new System.Windows.Forms.GroupBox();
		this.MAT_DCO_GROUP_4863_chkBox = new System.Windows.Forms.CheckBox();
		this.MAT_DCO_GROUP_3247_chkBox = new System.Windows.Forms.CheckBox();
		this.MAT_DCO_GROUP_1631_chkBox = new System.Windows.Forms.CheckBox();
		this.MAT_DCO_GROUP_0015_chkBox = new System.Windows.Forms.CheckBox();
		this.DAQreset_but = new System.Windows.Forms.Button();
		this.DCOcalib_but = new System.Windows.Forms.Button();
		this.MAT_DCO_GROUPS_label = new System.Windows.Forms.Label();
		this.MAT_CAL_SEL_DCO_label = new System.Windows.Forms.Label();
		this.MatI2C_read_all_but = new System.Windows.Forms.Button();
		this.MatI2C_write_all_but = new System.Windows.Forms.Button();
		this.MatI2C_read_single_but = new System.Windows.Forms.Button();
		this.MatI2C_write_single_but = new System.Windows.Forms.Button();
		this.Mat_dGridView = new System.Windows.Forms.DataGridView();
		this.Row_Col1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Row_Col2 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Row_Col3 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Row_Col4 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Row_Col5 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Row_Col6 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Row_Col7 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Row_Col8 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Row_Col9 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Row_Col10 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Row_Col11 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Row_Col12 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Row_Col13 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Row_Col14 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Row_Col15 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Row_Col16 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.MatAddr_comboBox = new System.Windows.Forms.ComboBox();
		this.MAT_I2C_addr_tBox = new System.Windows.Forms.TextBox();
		this.Top_tabPage = new System.Windows.Forms.TabPage();
		this.TopSelQuad_SE_chkBox = new System.Windows.Forms.CheckBox();
		this.TopSelQuad_NE_chkBox = new System.Windows.Forms.CheckBox();
		this.TopSelQuad_SW_chkBox = new System.Windows.Forms.CheckBox();
		this.TopSelQuad_NW_chkBox = new System.Windows.Forms.CheckBox();
		this.DaqFifoForm_but = new System.Windows.Forms.Button();
		this.TOP_COMMANDS_groupBox = new System.Windows.Forms.GroupBox();
		this.TOP_COMM_START_CAL_chkBox = new System.Windows.Forms.CheckBox();
		this.TOP_TDCpulse_but = new System.Windows.Forms.Button();
		this.TOP_TDCpulse_contextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
		this.TOP_TDCpulse_x32times = new System.Windows.Forms.ToolStripMenuItem();
		this.TOP_TDCpulse_x50times = new System.Windows.Forms.ToolStripMenuItem();
		this.TOP_TDCpulse_x64times = new System.Windows.Forms.ToolStripMenuItem();
		this.TOP_TDCpulse_x100times = new System.Windows.Forms.ToolStripMenuItem();
		this.TOP_TDCpulse_x128times = new System.Windows.Forms.ToolStripMenuItem();
		this.TOP_DAQreset_but = new System.Windows.Forms.Button();
		this.TOP_COMM_START_AUTO_chkBox = new System.Windows.Forms.CheckBox();
		this.TOP_COMM_FRC_RST_CAL_chkBox = new System.Windows.Forms.CheckBox();
		this.CAP_MEAS_groupBox = new System.Windows.Forms.GroupBox();
		this.CMES_CC_04F_UpDown = new System.Windows.Forms.NumericUpDown();
		this.CMES_CC_04F_label = new System.Windows.Forms.Label();
		this.CMES_CQ_20F_UpDown = new System.Windows.Forms.NumericUpDown();
		this.CMES_CQ_20F_label = new System.Windows.Forms.Label();
		this.CMES_CF_20F_UpDown = new System.Windows.Forms.NumericUpDown();
		this.CMES_CF_20F_label = new System.Windows.Forms.Label();
		this.CMES_DPOL_comboBox = new System.Windows.Forms.ComboBox();
		this.CMES_SEL_WAIT_UpDown = new System.Windows.Forms.NumericUpDown();
		this.CMES_SEL_WAIT_label = new System.Windows.Forms.Label();
		this.CMES_ARST_chkBox = new System.Windows.Forms.CheckBox();
		this.CMES_QPOL_comboBox = new System.Windows.Forms.ComboBox();
		this.CMES_DPOL_label = new System.Windows.Forms.Label();
		this.CMES_QPOL_label = new System.Windows.Forms.Label();
		this.CMES_DEN_chkBox = new System.Windows.Forms.CheckBox();
		this.CMES_AEN_chkBox = new System.Windows.Forms.CheckBox();
		this.AFE_PULSE_groupBox = new System.Windows.Forms.GroupBox();
		this.AFE_EOS_chkBox = new System.Windows.Forms.CheckBox();
		this.AFE_TP_REPETITION_UpDown = new System.Windows.Forms.NumericUpDown();
		this.AFE_TP_REPETITION_label = new System.Windows.Forms.Label();
		this.AFE_TP_WIDTH_UpDown = new System.Windows.Forms.NumericUpDown();
		this.AFE_TP_WIDTH_label = new System.Windows.Forms.Label();
		this.AFE_TP_PERIOD_UpDown = new System.Windows.Forms.NumericUpDown();
		this.AFE_TP_PERIOD_label = new System.Windows.Forms.Label();
		this.AFE_EN_TP_PHASE_UpDown = new System.Windows.Forms.NumericUpDown();
		this.AFE_LISTEN_TIME_UpDown = new System.Windows.Forms.NumericUpDown();
		this.AFE_UPDATE_TIME_UpDown = new System.Windows.Forms.NumericUpDown();
		this.AFE_EN_TP_PHASE_label = new System.Windows.Forms.Label();
		this.AFE_LISTEN_TIME_label = new System.Windows.Forms.Label();
		this.AFE_UPDATE_TIME_label = new System.Windows.Forms.Label();
		this.AFE_START_TP_chkBox = new System.Windows.Forms.CheckBox();
		this.GPO_OUT_SEL_groupBox = new System.Windows.Forms.GroupBox();
		this.FIXED_PATTERN_label = new System.Windows.Forms.Label();
		this.FIXED_PATTERN_UpDown = new System.Windows.Forms.NumericUpDown();
		this.READOUT_SER_label = new System.Windows.Forms.Label();
		this.GPO_SEL_label = new System.Windows.Forms.Label();
		this.SER_CK_SEL_comboBox = new System.Windows.Forms.ComboBox();
		this.SEL_RO_comboBox = new System.Windows.Forms.ComboBox();
		this.GPO_CMOS_SEL_comboBox = new System.Windows.Forms.ComboBox();
		this.GPO_SLVS_SEL_comboBox = new System.Windows.Forms.ComboBox();
		this.TDC_PULSE_groupBox = new System.Windows.Forms.GroupBox();
		this.POINT_TA_TOT_label = new System.Windows.Forms.Label();
		this.SEL_PULSE_SRC_label = new System.Windows.Forms.Label();
		this.POINT_TOT_UpDown = new System.Windows.Forms.NumericUpDown();
		this.POINT_TA_UpDown = new System.Windows.Forms.NumericUpDown();
		this.SEL_PULSE_SRC_comboBox = new System.Windows.Forms.ComboBox();
		this.TEST_POINT_label = new System.Windows.Forms.Label();
		this.BXID_groupBox = new System.Windows.Forms.GroupBox();
		this.BXID_LMT_label = new System.Windows.Forms.Label();
		this.BXID_PRL_label = new System.Windows.Forms.Label();
		this.BXID_LMT_UpDown = new System.Windows.Forms.NumericUpDown();
		this.BXID_PRL_UpDown = new System.Windows.Forms.NumericUpDown();
		this.IOSetSel_groupBox = new System.Windows.Forms.GroupBox();
		this.FE_POLARITY_label = new System.Windows.Forms.Label();
		this.SLVS_INVRX_chkBox = new System.Windows.Forms.CheckBox();
		this.SLVS_TRM_label = new System.Windows.Forms.Label();
		this.SLVS_DRV_STR_label = new System.Windows.Forms.Label();
		this.SLVS_CMM_MODE_label = new System.Windows.Forms.Label();
		this.FASTIN_EN_chkBox = new System.Windows.Forms.CheckBox();
		this.SLVS_INVTX_chkBox = new System.Windows.Forms.CheckBox();
		this.FE_POLARITY_comboBox = new System.Windows.Forms.ComboBox();
		this.SLVS_TRM_comboBox = new System.Windows.Forms.ComboBox();
		this.SLVS_DRV_STR_UpDown = new System.Windows.Forms.NumericUpDown();
		this.SLVS_CMM_MODE_UpDown = new System.Windows.Forms.NumericUpDown();
		this.TopAddr_comboBox = new System.Windows.Forms.ComboBox();
		this.TopI2C_read_all_but = new System.Windows.Forms.Button();
		this.TopI2C_write_all_but = new System.Windows.Forms.Button();
		this.TopI2C_read_single_but = new System.Windows.Forms.Button();
		this.TopI2C_write_single_but = new System.Windows.Forms.Button();
		this.TOP_I2C_addr_tBox = new System.Windows.Forms.TextBox();
		this.Top_dGridView = new System.Windows.Forms.DataGridView();
		this.Top_Col1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Top_Col2 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Top_Col3 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Top_Col4 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Top_Col5 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Top_Col6 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Top_Col7 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Top_Col8 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Top_Col9 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Top_Col10 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Top_Col11 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Top_Col12 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Top_Col13 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Top_Col14 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Top_Col15 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.Top_Col16 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.DEF_CONFIG_groupBox = new System.Windows.Forms.GroupBox();
		this.DEFAULT_CONFIG_chkBox = new System.Windows.Forms.CheckBox();
		this.tabControl1 = new System.Windows.Forms.TabControl();
		this.IOext_tabPage = new System.Windows.Forms.TabPage();
		this.I2Cmux_grpBox = new System.Windows.Forms.GroupBox();
		this.SE_i2c_chkBox = new System.Windows.Forms.CheckBox();
		this.NE_i2c_chkBox = new System.Windows.Forms.CheckBox();
		this.SW_i2c_chkBox = new System.Windows.Forms.CheckBox();
		this.NW_i2c_chkBox = new System.Windows.Forms.CheckBox();
		this.pictureBox1 = new System.Windows.Forms.PictureBox();
		this.Mux_I2C_CtrlReg_label = new System.Windows.Forms.Label();
		this.CtrlReg_tBox = new System.Windows.Forms.TextBox();
		this.Mux_I2C_read_but = new System.Windows.Forms.Button();
		this.Mux_I2C_write_but = new System.Windows.Forms.Button();
		this.I2CmuxAddr_comboBox = new System.Windows.Forms.ComboBox();
		this.Mux_I2C_addr_tBox = new System.Windows.Forms.TextBox();
		this.groupBox6 = new System.Windows.Forms.GroupBox();
		this.AnaPwr_chkBox = new System.Windows.Forms.CheckBox();
		this.IOexpRead_but = new System.Windows.Forms.Button();
		this.IOexpWrite_but = new System.Windows.Forms.Button();
		this.ExtDacEn_chkBox = new System.Windows.Forms.CheckBox();
		this.SiLol_chkBox = new System.Windows.Forms.CheckBox();
		this.IO_Board_ClockSel_label = new System.Windows.Forms.Label();
		this.SiClkInSrc_comboBox = new System.Windows.Forms.ComboBox();
		this.SICLKOE_chkBox = new System.Windows.Forms.CheckBox();
		this.IO_Board_SelDataEnSrc_label = new System.Windows.Forms.Label();
		this.SelDataEnSrc_comboBox = new System.Windows.Forms.ComboBox();
		this.IO_Board_FastIN_label = new System.Windows.Forms.Label();
		this.FastinSrc_comboBox = new System.Windows.Forms.ComboBox();
		this.IOextI2C_read_all_but = new System.Windows.Forms.Button();
		this.IOextI2C_write_all_but = new System.Windows.Forms.Button();
		this.IOextI2C_read_single_but = new System.Windows.Forms.Button();
		this.IOextI2C_write_single_but = new System.Windows.Forms.Button();
		this.IOext_dGridView = new System.Windows.Forms.DataGridView();
		this.dataGridViewTextBoxColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn2 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn3 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn4 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn5 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn6 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn7 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn8 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn9 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn10 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn11 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn12 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn13 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn14 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn15 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.dataGridViewTextBoxColumn16 = new System.Windows.Forms.DataGridViewTextBoxColumn();
		this.IOextAddr_comboBox = new System.Windows.Forms.ComboBox();
		this.IOext_I2C_addr_tBox = new System.Windows.Forms.TextBox();
		this.FIXED_PATTERN_textBox = new System.Windows.Forms.TextBox();
		this.TDCcalibAll_contMenuStrip.SuspendLayout();
		this.TDCwriteAll_contMenuStrip.SuspendLayout();
		this.menuStrip1.SuspendLayout();
		this.statusStrip1.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.SEL_CAL_TIME_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.PIX_Sel_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DCO0ctrl_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DCO0adj_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DCO_PIX_adj_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DCO_PIX_ctrl_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_FT_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_VTH_H_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_FT_SEL_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_VTH_L_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_VINJ_H_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_VINJ_L_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_VLDO_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_VFB_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DAC_IDISC_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DAC_ICSA_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DAC_IKRUM_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.CH_SEL_42_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.CH_SEL_41_UpDown).BeginInit();
		this.exAdcDac_tabPage.SuspendLayout();
		this.Quad_DAC_grpBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.DACicap_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DACiref_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DACvfeed_UpDown).BeginInit();
		this.groupBox1.SuspendLayout();
		this.groupBox2.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.ScanVthStep_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.ScanVthMin_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.ScanVthMax_UpDown).BeginInit();
		this.groupBox4.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.ScanVinj2Step_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.ScanVinj2Min_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.ScanVinj2Max_UpDown).BeginInit();
		this.Quad_ADC_grpBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.ADCdac_Quad_NShot_numUpDown).BeginInit();
		this.CurMonADC_groupBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.ADCcommon_NShot_numUpDown).BeginInit();
		this.ExtDAC_groupBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.DACvref_L_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DACvthr_H_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DACvthr_L_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DACvinj_H_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.DACVext_UpDown).BeginInit();
		this.Ctrl_tabPage.SuspendLayout();
		this.groupBox3.SuspendLayout();
		this.Mat_tabPage.SuspendLayout();
		this.MAT_Test_Routines_groupBox.SuspendLayout();
		this.MAT_AFE_groupBox.SuspendLayout();
		this.PIX_groupBox.SuspendLayout();
		this.MAT_DAC_groupBox.SuspendLayout();
		this.MAT_cfg_groupBox.SuspendLayout();
		this.MAT_COMMANDS_groupBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.Mat_dGridView).BeginInit();
		this.Top_tabPage.SuspendLayout();
		this.TOP_COMMANDS_groupBox.SuspendLayout();
		this.TOP_TDCpulse_contextMenuStrip.SuspendLayout();
		this.CAP_MEAS_groupBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.CMES_CC_04F_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.CMES_CQ_20F_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.CMES_CF_20F_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.CMES_SEL_WAIT_UpDown).BeginInit();
		this.AFE_PULSE_groupBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.AFE_TP_REPETITION_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.AFE_TP_WIDTH_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.AFE_TP_PERIOD_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.AFE_EN_TP_PHASE_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.AFE_LISTEN_TIME_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.AFE_UPDATE_TIME_UpDown).BeginInit();
		this.GPO_OUT_SEL_groupBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.FIXED_PATTERN_UpDown).BeginInit();
		this.TDC_PULSE_groupBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.POINT_TOT_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.POINT_TA_UpDown).BeginInit();
		this.BXID_groupBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.BXID_LMT_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.BXID_PRL_UpDown).BeginInit();
		this.IOSetSel_groupBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.SLVS_DRV_STR_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.SLVS_CMM_MODE_UpDown).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.Top_dGridView).BeginInit();
		this.DEF_CONFIG_groupBox.SuspendLayout();
		this.tabControl1.SuspendLayout();
		this.IOext_tabPage.SuspendLayout();
		this.I2Cmux_grpBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox1).BeginInit();
		this.groupBox6.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.IOext_dGridView).BeginInit();
		base.SuspendLayout();
		this.TDCcalibAll_contMenuStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
		this.TDCcalibAll_contMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[3] { this.calibALLTDCToolStripMenuItem, this.calibCol0TDCsToolStripMenuItem, this.calibCol1TDCsToolStripMenuItem });
		this.TDCcalibAll_contMenuStrip.Name = "TDCcalibAll_contMenuStrip";
		this.TDCcalibAll_contMenuStrip.Size = new System.Drawing.Size(159, 70);
		this.calibALLTDCToolStripMenuItem.Name = "calibALLTDCToolStripMenuItem";
		this.calibALLTDCToolStripMenuItem.Size = new System.Drawing.Size(158, 22);
		this.calibALLTDCToolStripMenuItem.Text = "Calib ALL TDCs";
		this.calibALLTDCToolStripMenuItem.Click += new System.EventHandler(TDCcalibAll_contMenuStrip_Click);
		this.calibCol0TDCsToolStripMenuItem.Name = "calibCol0TDCsToolStripMenuItem";
		this.calibCol0TDCsToolStripMenuItem.Size = new System.Drawing.Size(158, 22);
		this.calibCol0TDCsToolStripMenuItem.Text = "Calib Col0 TDCs";
		this.calibCol0TDCsToolStripMenuItem.Click += new System.EventHandler(TDCcalibAll_contMenuStrip_Click);
		this.calibCol1TDCsToolStripMenuItem.Name = "calibCol1TDCsToolStripMenuItem";
		this.calibCol1TDCsToolStripMenuItem.Size = new System.Drawing.Size(158, 22);
		this.calibCol1TDCsToolStripMenuItem.Text = "Calib Col1 TDCs";
		this.calibCol1TDCsToolStripMenuItem.Click += new System.EventHandler(TDCcalibAll_contMenuStrip_Click);
		this.TDCwriteAll_contMenuStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
		this.TDCwriteAll_contMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[3] { this.writeALLTDCsToolStripMenuItem, this.writeTDCsCol0ToolStripMenuItem, this.writeTDCsCol1ToolStripMenuItem });
		this.TDCwriteAll_contMenuStrip.Name = "TDCwriteAll_contMenuStrip";
		this.TDCwriteAll_contMenuStrip.Size = new System.Drawing.Size(160, 70);
		this.TDCwriteAll_contMenuStrip.Click += new System.EventHandler(TDCwriteAll_contMenuStrip_Click);
		this.writeALLTDCsToolStripMenuItem.Name = "writeALLTDCsToolStripMenuItem";
		this.writeALLTDCsToolStripMenuItem.Size = new System.Drawing.Size(159, 22);
		this.writeALLTDCsToolStripMenuItem.Text = "Write ALL TDCs";
		this.writeALLTDCsToolStripMenuItem.Click += new System.EventHandler(TDCwriteAll_contMenuStrip_Click);
		this.writeTDCsCol0ToolStripMenuItem.Name = "writeTDCsCol0ToolStripMenuItem";
		this.writeTDCsCol0ToolStripMenuItem.Size = new System.Drawing.Size(159, 22);
		this.writeTDCsCol0ToolStripMenuItem.Text = "Write TDCs Col0";
		this.writeTDCsCol0ToolStripMenuItem.Click += new System.EventHandler(TDCwriteAll_contMenuStrip_Click);
		this.writeTDCsCol1ToolStripMenuItem.Name = "writeTDCsCol1ToolStripMenuItem";
		this.writeTDCsCol1ToolStripMenuItem.Size = new System.Drawing.Size(159, 22);
		this.writeTDCsCol1ToolStripMenuItem.Text = "Write TDCs Col1";
		this.writeTDCsCol1ToolStripMenuItem.Click += new System.EventHandler(TDCwriteAll_contMenuStrip_Click);
		this.menuStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
		this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[7] { this.fileToolStripMenuItem, this.i2cToolsToolStripMenuItem, this.sI5341ToolStripMenuItem, this.globalsToolStripMenuItem, this.tDCToolStripMenuItem, this.plotResultsToolStripMenuItem, this.infoToolStripMenuItem1 });
		this.menuStrip1.Location = new System.Drawing.Point(0, 0);
		this.menuStrip1.Name = "menuStrip1";
		this.menuStrip1.Padding = new System.Windows.Forms.Padding(4, 2, 0, 2);
		this.menuStrip1.Size = new System.Drawing.Size(978, 24);
		this.menuStrip1.TabIndex = 2;
		this.menuStrip1.Text = "menuStrip1";
		this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[7] { this.loadConfigToolStripMenuItem, this.saveConfigToolStripMenuItem, this.toolStripMenuItem6, this.saveLogToolStripMenuItem, this.clearLogToolStripMenuItem, this.toolStripMenuItem1, this.exitToolStripMenuItem });
		this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
		this.fileToolStripMenuItem.Size = new System.Drawing.Size(37, 20);
		this.fileToolStripMenuItem.Text = "File";
		this.loadConfigToolStripMenuItem.Name = "loadConfigToolStripMenuItem";
		this.loadConfigToolStripMenuItem.Size = new System.Drawing.Size(139, 22);
		this.loadConfigToolStripMenuItem.Text = "Load Config";
		this.loadConfigToolStripMenuItem.Click += new System.EventHandler(loadConfigToolStripMenuItem_Click);
		this.saveConfigToolStripMenuItem.Name = "saveConfigToolStripMenuItem";
		this.saveConfigToolStripMenuItem.Size = new System.Drawing.Size(139, 22);
		this.saveConfigToolStripMenuItem.Text = "Save Config";
		this.saveConfigToolStripMenuItem.Click += new System.EventHandler(saveConfigToolStripMenuItem_Click);
		this.toolStripMenuItem6.Name = "toolStripMenuItem6";
		this.toolStripMenuItem6.Size = new System.Drawing.Size(136, 6);
		this.saveLogToolStripMenuItem.Name = "saveLogToolStripMenuItem";
		this.saveLogToolStripMenuItem.Size = new System.Drawing.Size(139, 22);
		this.saveLogToolStripMenuItem.Text = "Save Log";
		this.saveLogToolStripMenuItem.Click += new System.EventHandler(saveLogToolStripMenuItem_Click);
		this.clearLogToolStripMenuItem.Name = "clearLogToolStripMenuItem";
		this.clearLogToolStripMenuItem.Size = new System.Drawing.Size(139, 22);
		this.clearLogToolStripMenuItem.Text = "Clear Log";
		this.clearLogToolStripMenuItem.Click += new System.EventHandler(clearLogToolStripMenuItem_Click);
		this.toolStripMenuItem1.Name = "toolStripMenuItem1";
		this.toolStripMenuItem1.Size = new System.Drawing.Size(136, 6);
		this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
		this.exitToolStripMenuItem.Size = new System.Drawing.Size(139, 22);
		this.exitToolStripMenuItem.Text = "Exit";
		this.exitToolStripMenuItem.Click += new System.EventHandler(exitToolStripMenuItem_Click);
		this.i2cToolsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[8] { this.infoToolStripMenuItem, this.selectDevicesToolStripMenuItem, this.setI2CFrequencyToolStripMenuItem, this.BusRecoveryToolStripMenuItem, this.toolStripMenuItem4, this.scanI2CBusToolStripMenuItem, this.toolStripSeparator1, this.debugModeToolStripMenuItem });
		this.i2cToolsToolStripMenuItem.Name = "i2cToolsToolStripMenuItem";
		this.i2cToolsToolStripMenuItem.Size = new System.Drawing.Size(63, 20);
		this.i2cToolsToolStripMenuItem.Text = "i2c tools";
		this.infoToolStripMenuItem.Name = "infoToolStripMenuItem";
		this.infoToolStripMenuItem.Size = new System.Drawing.Size(168, 22);
		this.infoToolStripMenuItem.Text = "Info";
		this.infoToolStripMenuItem.Click += new System.EventHandler(infoToolStripMenuItem_Click);
		this.selectDevicesToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[1] { this.SelDevToolStripComboBox });
		this.selectDevicesToolStripMenuItem.Name = "selectDevicesToolStripMenuItem";
		this.selectDevicesToolStripMenuItem.Size = new System.Drawing.Size(168, 22);
		this.selectDevicesToolStripMenuItem.Text = "Select Devices";
		this.SelDevToolStripComboBox.Name = "SelDevToolStripComboBox";
		this.SelDevToolStripComboBox.Size = new System.Drawing.Size(121, 23);
		this.SelDevToolStripComboBox.SelectedIndexChanged += new System.EventHandler(SelDevToolStripComboBox_SelectedIndexChanged);
		this.setI2CFrequencyToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[3] { this.kHz100ToolStripMenuItem, this.kHz500ToolStripMenuItem, this.mHzToolStripMenuItem });
		this.setI2CFrequencyToolStripMenuItem.Name = "setI2CFrequencyToolStripMenuItem";
		this.setI2CFrequencyToolStripMenuItem.Size = new System.Drawing.Size(168, 22);
		this.setI2CFrequencyToolStripMenuItem.Text = "Set I2C Frequency";
		this.kHz100ToolStripMenuItem.Name = "kHz100ToolStripMenuItem";
		this.kHz100ToolStripMenuItem.Size = new System.Drawing.Size(113, 22);
		this.kHz100ToolStripMenuItem.Text = "100KHz";
		this.kHz100ToolStripMenuItem.Click += new System.EventHandler(setI2CFrequencyToolStripMenuItem_Click);
		this.kHz500ToolStripMenuItem.Name = "kHz500ToolStripMenuItem";
		this.kHz500ToolStripMenuItem.Size = new System.Drawing.Size(113, 22);
		this.kHz500ToolStripMenuItem.Text = "500KHz";
		this.kHz500ToolStripMenuItem.Click += new System.EventHandler(setI2CFrequencyToolStripMenuItem_Click);
		this.mHzToolStripMenuItem.Name = "mHzToolStripMenuItem";
		this.mHzToolStripMenuItem.Size = new System.Drawing.Size(113, 22);
		this.mHzToolStripMenuItem.Text = "1MHz";
		this.mHzToolStripMenuItem.Click += new System.EventHandler(setI2CFrequencyToolStripMenuItem_Click);
		this.BusRecoveryToolStripMenuItem.Name = "BusRecoveryToolStripMenuItem";
		this.BusRecoveryToolStripMenuItem.Size = new System.Drawing.Size(168, 22);
		this.BusRecoveryToolStripMenuItem.Text = "Bus Recovery";
		this.BusRecoveryToolStripMenuItem.Click += new System.EventHandler(setI2CFrequencyToolStripMenuItem_Click);
		this.toolStripMenuItem4.Name = "toolStripMenuItem4";
		this.toolStripMenuItem4.Size = new System.Drawing.Size(165, 6);
		this.scanI2CBusToolStripMenuItem.Name = "scanI2CBusToolStripMenuItem";
		this.scanI2CBusToolStripMenuItem.Size = new System.Drawing.Size(168, 22);
		this.scanI2CBusToolStripMenuItem.Text = "Scan I2C bus";
		this.scanI2CBusToolStripMenuItem.Click += new System.EventHandler(scanI2CbusToolStripMenuItem_Click);
		this.toolStripSeparator1.Name = "toolStripSeparator1";
		this.toolStripSeparator1.Size = new System.Drawing.Size(165, 6);
		this.debugModeToolStripMenuItem.Name = "debugModeToolStripMenuItem";
		this.debugModeToolStripMenuItem.Size = new System.Drawing.Size(168, 22);
		this.debugModeToolStripMenuItem.Text = "Debug Mode";
		this.debugModeToolStripMenuItem.Click += new System.EventHandler(debugModeToolStripMenuItem_Click);
		this.sI5341ToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[2] { this.runWizardToolStripMenuItem, this.loadConfigFileToolStripMenuItem });
		this.sI5341ToolStripMenuItem.Name = "sI5341ToolStripMenuItem";
		this.sI5341ToolStripMenuItem.Size = new System.Drawing.Size(52, 20);
		this.sI5341ToolStripMenuItem.Text = "SI5340";
		this.runWizardToolStripMenuItem.Name = "runWizardToolStripMenuItem";
		this.runWizardToolStripMenuItem.Size = new System.Drawing.Size(158, 22);
		this.runWizardToolStripMenuItem.Text = "Run Wizard";
		this.runWizardToolStripMenuItem.Click += new System.EventHandler(runWizardToolStripMenuItem_Click);
		this.loadConfigFileToolStripMenuItem.Name = "loadConfigFileToolStripMenuItem";
		this.loadConfigFileToolStripMenuItem.Size = new System.Drawing.Size(158, 22);
		this.loadConfigFileToolStripMenuItem.Text = "Load Config file";
		this.loadConfigFileToolStripMenuItem.Click += new System.EventHandler(loadSIConfigToolStripMenuItem_Click);
		this.globalsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[5] { this.resetToolStripMenuItem, this.toolStripMenuItem2, this.initializingToolStripMenuItem, this.toolStripMenuItem3, this.rowToolStripMenuItem });
		this.globalsToolStripMenuItem.Name = "globalsToolStripMenuItem";
		this.globalsToolStripMenuItem.Size = new System.Drawing.Size(58, 20);
		this.globalsToolStripMenuItem.Text = "Globals";
		this.resetToolStripMenuItem.Name = "resetToolStripMenuItem";
		this.resetToolStripMenuItem.Size = new System.Drawing.Size(128, 22);
		this.resetToolStripMenuItem.Text = "Reset";
		this.resetToolStripMenuItem.Click += new System.EventHandler(resetToolStripMenuItem_Click);
		this.toolStripMenuItem2.Name = "toolStripMenuItem2";
		this.toolStripMenuItem2.Size = new System.Drawing.Size(125, 6);
		this.initializingToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[2] { this.externalDACsToolStripMenuItem, this.internalDACsToolStripMenuItem });
		this.initializingToolStripMenuItem.Name = "initializingToolStripMenuItem";
		this.initializingToolStripMenuItem.Size = new System.Drawing.Size(128, 22);
		this.initializingToolStripMenuItem.Text = "Initializing";
		this.externalDACsToolStripMenuItem.Name = "externalDACsToolStripMenuItem";
		this.externalDACsToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
		this.externalDACsToolStripMenuItem.Text = "External DACs";
		this.internalDACsToolStripMenuItem.Name = "internalDACsToolStripMenuItem";
		this.internalDACsToolStripMenuItem.Size = new System.Drawing.Size(148, 22);
		this.internalDACsToolStripMenuItem.Text = "Internal DACs";
		this.toolStripMenuItem3.Name = "toolStripMenuItem3";
		this.toolStripMenuItem3.Size = new System.Drawing.Size(125, 6);
		this.rowToolStripMenuItem.Name = "rowToolStripMenuItem";
		this.rowToolStripMenuItem.Size = new System.Drawing.Size(128, 22);
		this.rowToolStripMenuItem.Text = "ROWs";
		this.tDCToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[2] { this.TDCautoTestToolStripMenuItem, this.tDCDataRawToolStripMenuItem });
		this.tDCToolStripMenuItem.Name = "tDCToolStripMenuItem";
		this.tDCToolStripMenuItem.Size = new System.Drawing.Size(44, 20);
		this.tDCToolStripMenuItem.Text = "Tests";
		this.TDCautoTestToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[2] { this.dCOTestToolStripMenuItem, this.tDCTestToolStripMenuItem });
		this.TDCautoTestToolStripMenuItem.Name = "TDCautoTestToolStripMenuItem";
		this.TDCautoTestToolStripMenuItem.Size = new System.Drawing.Size(144, 22);
		this.TDCautoTestToolStripMenuItem.Text = "TDC Tests";
		this.dCOTestToolStripMenuItem.Name = "dCOTestToolStripMenuItem";
		this.dCOTestToolStripMenuItem.Size = new System.Drawing.Size(121, 22);
		this.dCOTestToolStripMenuItem.Text = "DCO test";
		this.dCOTestToolStripMenuItem.Click += new System.EventHandler(dCOTestToolStripMenuItem_Click);
		this.tDCTestToolStripMenuItem.Name = "tDCTestToolStripMenuItem";
		this.tDCTestToolStripMenuItem.Size = new System.Drawing.Size(121, 22);
		this.tDCTestToolStripMenuItem.Text = "TDC test";
		this.tDCTestToolStripMenuItem.Click += new System.EventHandler(TDCautoTestToolStripMenuItem_Click);
		this.tDCDataRawToolStripMenuItem.Name = "tDCDataRawToolStripMenuItem";
		this.tDCDataRawToolStripMenuItem.Size = new System.Drawing.Size(144, 22);
		this.tDCDataRawToolStripMenuItem.Text = "TDC data raw";
		this.tDCDataRawToolStripMenuItem.Click += new System.EventHandler(tDCDataRawToolStripMenuItem_Click);
		this.plotResultsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[1] { this.plotResultsToolStripMenuItem1 });
		this.plotResultsToolStripMenuItem.Name = "plotResultsToolStripMenuItem";
		this.plotResultsToolStripMenuItem.Size = new System.Drawing.Size(80, 20);
		this.plotResultsToolStripMenuItem.Text = "Plot Results";
		this.plotResultsToolStripMenuItem1.Name = "plotResultsToolStripMenuItem1";
		this.plotResultsToolStripMenuItem1.Size = new System.Drawing.Size(135, 22);
		this.plotResultsToolStripMenuItem1.Text = "Plot Results";
		this.plotResultsToolStripMenuItem1.Click += new System.EventHandler(plotResult_Click);
		this.infoToolStripMenuItem1.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[2] { this.ignite0LayoutMapToolStripMenuItem, this.viewDatasheetToolStripMenuItem });
		this.infoToolStripMenuItem1.Name = "infoToolStripMenuItem1";
		this.infoToolStripMenuItem1.Size = new System.Drawing.Size(40, 20);
		this.infoToolStripMenuItem1.Text = "Info";
		this.ignite0LayoutMapToolStripMenuItem.Name = "ignite0LayoutMapToolStripMenuItem";
		this.ignite0LayoutMapToolStripMenuItem.Size = new System.Drawing.Size(182, 22);
		this.ignite0LayoutMapToolStripMenuItem.Text = "ignite32 Layout Map";
		this.ignite0LayoutMapToolStripMenuItem.Click += new System.EventHandler(ignite32LayoutMapToolStripMenuItem_Click);
		this.viewDatasheetToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[5] { this.ignite32ToolStripMenuItem, this.pCBSchemesToolStripMenuItem, this.aDCToolStripMenuItem, this.dACToolStripMenuItem, this.iOExpanderToolStripMenuItem });
		this.viewDatasheetToolStripMenuItem.Name = "viewDatasheetToolStripMenuItem";
		this.viewDatasheetToolStripMenuItem.Size = new System.Drawing.Size(182, 22);
		this.viewDatasheetToolStripMenuItem.Text = "View Datasheet";
		this.ignite32ToolStripMenuItem.Name = "ignite32ToolStripMenuItem";
		this.ignite32ToolStripMenuItem.Size = new System.Drawing.Size(145, 22);
		this.ignite32ToolStripMenuItem.Text = "Ignite32";
		this.ignite32ToolStripMenuItem.Click += new System.EventHandler(viewDatasheetToolStripMenuItem_Click);
		this.pCBSchemesToolStripMenuItem.Name = "pCBSchemesToolStripMenuItem";
		this.pCBSchemesToolStripMenuItem.Size = new System.Drawing.Size(145, 22);
		this.pCBSchemesToolStripMenuItem.Text = "PCB schemes";
		this.pCBSchemesToolStripMenuItem.Click += new System.EventHandler(viewDatasheetToolStripMenuItem_Click);
		this.aDCToolStripMenuItem.Name = "aDCToolStripMenuItem";
		this.aDCToolStripMenuItem.Size = new System.Drawing.Size(145, 22);
		this.aDCToolStripMenuItem.Text = "ADC";
		this.aDCToolStripMenuItem.Click += new System.EventHandler(viewDatasheetToolStripMenuItem_Click);
		this.dACToolStripMenuItem.Name = "dACToolStripMenuItem";
		this.dACToolStripMenuItem.Size = new System.Drawing.Size(145, 22);
		this.dACToolStripMenuItem.Text = "DAC";
		this.dACToolStripMenuItem.Click += new System.EventHandler(viewDatasheetToolStripMenuItem_Click);
		this.iOExpanderToolStripMenuItem.Name = "iOExpanderToolStripMenuItem";
		this.iOExpanderToolStripMenuItem.Size = new System.Drawing.Size(145, 22);
		this.iOExpanderToolStripMenuItem.Text = "IO Expander";
		this.statusStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
		this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[7] { this.BuiltData_tSSLabel, this.HW_detect_tSSLabel, this.I2C_freq_tSSLabel, this.DevSerial_tSSLabel, this.AnalogPowerStatus_tSSLabel, this.MuxStatus_tSSLabel, this.I2C_oper_tSSLabel });
		this.statusStrip1.Location = new System.Drawing.Point(0, 583);
		this.statusStrip1.Name = "statusStrip1";
		this.statusStrip1.Size = new System.Drawing.Size(978, 24);
		this.statusStrip1.TabIndex = 3;
		this.statusStrip1.Text = "statusStrip1";
		this.BuiltData_tSSLabel.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left | System.Windows.Forms.ToolStripStatusLabelBorderSides.Right;
		this.BuiltData_tSSLabel.Name = "BuiltData_tSSLabel";
		this.BuiltData_tSSLabel.Size = new System.Drawing.Size(62, 19);
		this.BuiltData_tSSLabel.Text = "Built Data";
		this.BuiltData_tSSLabel.ToolTipText = "Built data";
		this.HW_detect_tSSLabel.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left | System.Windows.Forms.ToolStripStatusLabelBorderSides.Right;
		this.HW_detect_tSSLabel.Name = "HW_detect_tSSLabel";
		this.HW_detect_tSSLabel.Size = new System.Drawing.Size(81, 19);
		this.HW_detect_tSSLabel.Text = "HW Detected";
		this.I2C_freq_tSSLabel.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left | System.Windows.Forms.ToolStripStatusLabelBorderSides.Right;
		this.I2C_freq_tSSLabel.Name = "I2C_freq_tSSLabel";
		this.I2C_freq_tSSLabel.Size = new System.Drawing.Size(84, 19);
		this.I2C_freq_tSSLabel.Text = "I2C frequency";
		this.DevSerial_tSSLabel.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left | System.Windows.Forms.ToolStripStatusLabelBorderSides.Right;
		this.DevSerial_tSSLabel.Name = "DevSerial_tSSLabel";
		this.DevSerial_tSSLabel.Size = new System.Drawing.Size(77, 19);
		this.DevSerial_tSSLabel.Text = "Device Serial";
		this.AnalogPowerStatus_tSSLabel.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left | System.Windows.Forms.ToolStripStatusLabelBorderSides.Right;
		this.AnalogPowerStatus_tSSLabel.Name = "AnalogPowerStatus_tSSLabel";
		this.AnalogPowerStatus_tSSLabel.Size = new System.Drawing.Size(99, 19);
		this.AnalogPowerStatus_tSSLabel.Text = "Analog Pwr: OFF";
		this.MuxStatus_tSSLabel.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left | System.Windows.Forms.ToolStripStatusLabelBorderSides.Right;
		this.MuxStatus_tSSLabel.Name = "MuxStatus_tSSLabel";
		this.MuxStatus_tSSLabel.Size = new System.Drawing.Size(70, 19);
		this.MuxStatus_tSSLabel.Text = "Mux Status";
		this.I2C_oper_tSSLabel.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left | System.Windows.Forms.ToolStripStatusLabelBorderSides.Right;
		this.I2C_oper_tSSLabel.Name = "I2C_oper_tSSLabel";
		this.I2C_oper_tSSLabel.Size = new System.Drawing.Size(121, 19);
		this.I2C_oper_tSSLabel.Text = "I2C Operation results";
		this.Log_textBox.Location = new System.Drawing.Point(16, 475);
		this.Log_textBox.MaxLength = int.MaxValue;
		this.Log_textBox.Multiline = true;
		this.Log_textBox.Name = "Log_textBox";
		this.Log_textBox.ScrollBars = System.Windows.Forms.ScrollBars.Both;
		this.Log_textBox.Size = new System.Drawing.Size(946, 102);
		this.Log_textBox.TabIndex = 4;
		this.Log_textBox.WordWrap = false;
		this.saveLogFileDialog.DefaultExt = "txt";
		this.saveLogFileDialog.Filter = "Text File|*.txt|All Files|*.*";
		this.saveLogFileDialog.InitialDirectory = ".";
		this.saveLogFileDialog.Title = "Save Log";
		this.saveFileDialog1.DefaultExt = "txt";
		this.saveFileDialog1.Filter = "Text File|*.txt|All Files|*.*";
		this.SEL_CAL_TIME_UpDown.Location = new System.Drawing.Point(164, 55);
		this.SEL_CAL_TIME_UpDown.Maximum = new decimal(new int[4] { 3, 0, 0, 0 });
		this.SEL_CAL_TIME_UpDown.Name = "SEL_CAL_TIME_UpDown";
		this.SEL_CAL_TIME_UpDown.Size = new System.Drawing.Size(30, 20);
		this.SEL_CAL_TIME_UpDown.TabIndex = 6;
		this.toolTip1.SetToolTip(this.SEL_CAL_TIME_UpDown, "Time Integration for Calib:\r\n400 ns\r\n800 ns\r\n1.6 us\r\n3.2 us");
		this.SEL_CAL_TIME_UpDown.ValueChanged += new System.EventHandler(config_mode_change);
		this.TDC_ON_chkBox.AutoSize = true;
		this.TDC_ON_chkBox.Location = new System.Drawing.Point(1, 15);
		this.TDC_ON_chkBox.Name = "TDC_ON_chkBox";
		this.TDC_ON_chkBox.Size = new System.Drawing.Size(67, 17);
		this.TDC_ON_chkBox.TabIndex = 3;
		this.TDC_ON_chkBox.Text = "TDC ON";
		this.toolTip1.SetToolTip(this.TDC_ON_chkBox, "Enable OC");
		this.TDC_ON_chkBox.UseVisualStyleBackColor = true;
		this.TDC_ON_chkBox.CheckedChanged += new System.EventHandler(config_mode_change);
		this.EN_TIMEOUT_chkBox.AutoSize = true;
		this.EN_TIMEOUT_chkBox.Location = new System.Drawing.Point(1, 34);
		this.EN_TIMEOUT_chkBox.Name = "EN_TIMEOUT_chkBox";
		this.EN_TIMEOUT_chkBox.Size = new System.Drawing.Size(83, 17);
		this.EN_TIMEOUT_chkBox.TabIndex = 2;
		this.EN_TIMEOUT_chkBox.Text = "En. Timeout";
		this.toolTip1.SetToolTip(this.EN_TIMEOUT_chkBox, "Enable Timeout");
		this.EN_TIMEOUT_chkBox.UseVisualStyleBackColor = true;
		this.EN_TIMEOUT_chkBox.CheckedChanged += new System.EventHandler(config_mode_change);
		this.enDEtot_chkBox.AutoSize = true;
		this.enDEtot_chkBox.Location = new System.Drawing.Point(77, 13);
		this.enDEtot_chkBox.Name = "enDEtot_chkBox";
		this.enDEtot_chkBox.Size = new System.Drawing.Size(85, 17);
		this.enDEtot_chkBox.TabIndex = 1;
		this.enDEtot_chkBox.Text = "En. DE TOT";
		this.toolTip1.SetToolTip(this.enDEtot_chkBox, "Enable Double Edge Counter fo TOT");
		this.enDEtot_chkBox.UseVisualStyleBackColor = true;
		this.enDEtot_chkBox.CheckedChanged += new System.EventHandler(config_mode_change);
		this.CAL_MODE_chkBox.AutoSize = true;
		this.CAL_MODE_chkBox.Location = new System.Drawing.Point(1, 57);
		this.CAL_MODE_chkBox.Name = "CAL_MODE_chkBox";
		this.CAL_MODE_chkBox.Size = new System.Drawing.Size(111, 17);
		this.CAL_MODE_chkBox.TabIndex = 0;
		this.CAL_MODE_chkBox.Text = "Enable Calibration";
		this.toolTip1.SetToolTip(this.CAL_MODE_chkBox, "Select the DCO to calib:\r\nunchecked -> DCO0\r\nchecked -> DCO1");
		this.CAL_MODE_chkBox.UseVisualStyleBackColor = true;
		this.CAL_MODE_chkBox.CheckedChanged += new System.EventHandler(config_mode_change);
		this.PIX_Sel_UpDown.Location = new System.Drawing.Point(65, 16);
		this.PIX_Sel_UpDown.Maximum = new decimal(new int[4] { 63, 0, 0, 0 });
		this.PIX_Sel_UpDown.Name = "PIX_Sel_UpDown";
		this.PIX_Sel_UpDown.Size = new System.Drawing.Size(39, 20);
		this.PIX_Sel_UpDown.TabIndex = 23;
		this.toolTip1.SetToolTip(this.PIX_Sel_UpDown, "Pixel  Selector");
		this.PIX_Sel_UpDown.ValueChanged += new System.EventHandler(PIXsetNum_change);
		this.PIX_ON_ALL_chkBox.AutoSize = true;
		this.PIX_ON_ALL_chkBox.Location = new System.Drawing.Point(4, 42);
		this.PIX_ON_ALL_chkBox.Name = "PIX_ON_ALL_chkBox";
		this.PIX_ON_ALL_chkBox.Size = new System.Drawing.Size(77, 17);
		this.PIX_ON_ALL_chkBox.TabIndex = 25;
		this.PIX_ON_ALL_chkBox.Text = "Edit all PIX";
		this.toolTip1.SetToolTip(this.PIX_ON_ALL_chkBox, "Turn ON All Pixels at once");
		this.PIX_ON_ALL_chkBox.UseVisualStyleBackColor = true;
		this.DCO0ctrl_UpDown.Location = new System.Drawing.Point(108, 85);
		this.DCO0ctrl_UpDown.Maximum = new decimal(new int[4] { 15, 0, 0, 0 });
		this.DCO0ctrl_UpDown.Name = "DCO0ctrl_UpDown";
		this.DCO0ctrl_UpDown.Size = new System.Drawing.Size(36, 20);
		this.DCO0ctrl_UpDown.TabIndex = 26;
		this.toolTip1.SetToolTip(this.DCO0ctrl_UpDown, "Set Phase for TA pulse");
		this.DCO0ctrl_UpDown.ValueChanged += new System.EventHandler(config_mode_change);
		this.DCO0adj_UpDown.Location = new System.Drawing.Point(76, 85);
		this.DCO0adj_UpDown.Maximum = new decimal(new int[4] { 3, 0, 0, 0 });
		this.DCO0adj_UpDown.Name = "DCO0adj_UpDown";
		this.DCO0adj_UpDown.Size = new System.Drawing.Size(32, 20);
		this.DCO0adj_UpDown.TabIndex = 27;
		this.toolTip1.SetToolTip(this.DCO0adj_UpDown, "Set Phase for TA pulse");
		this.DCO0adj_UpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.DCO0adj_UpDown.ValueChanged += new System.EventHandler(config_mode_change);
		this.DCO_PIX_adj_UpDown.Location = new System.Drawing.Point(4, 101);
		this.DCO_PIX_adj_UpDown.Maximum = new decimal(new int[4] { 3, 0, 0, 0 });
		this.DCO_PIX_adj_UpDown.Name = "DCO_PIX_adj_UpDown";
		this.DCO_PIX_adj_UpDown.Size = new System.Drawing.Size(34, 20);
		this.DCO_PIX_adj_UpDown.TabIndex = 31;
		this.toolTip1.SetToolTip(this.DCO_PIX_adj_UpDown, "Set Phase for TA pulse");
		this.DCO_PIX_adj_UpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.DCO_PIX_adj_UpDown.ValueChanged += new System.EventHandler(PIXset_change);
		this.DCO_PIX_ctrl_UpDown.Location = new System.Drawing.Point(38, 101);
		this.DCO_PIX_ctrl_UpDown.Maximum = new decimal(new int[4] { 15, 0, 0, 0 });
		this.DCO_PIX_ctrl_UpDown.Name = "DCO_PIX_ctrl_UpDown";
		this.DCO_PIX_ctrl_UpDown.Size = new System.Drawing.Size(36, 20);
		this.DCO_PIX_ctrl_UpDown.TabIndex = 30;
		this.toolTip1.SetToolTip(this.DCO_PIX_ctrl_UpDown, "Set Phase for TA pulse");
		this.DCO_PIX_ctrl_UpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.DCO_PIX_ctrl_UpDown.ValueChanged += new System.EventHandler(PIXset_change);
		this.PIX_DCO_ALL_chkBox.AutoSize = true;
		this.PIX_DCO_ALL_chkBox.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.PIX_DCO_ALL_chkBox.Location = new System.Drawing.Point(74, 87);
		this.PIX_DCO_ALL_chkBox.Name = "PIX_DCO_ALL_chkBox";
		this.PIX_DCO_ALL_chkBox.Size = new System.Drawing.Size(28, 31);
		this.PIX_DCO_ALL_chkBox.TabIndex = 35;
		this.PIX_DCO_ALL_chkBox.Text = "All?";
		this.toolTip1.SetToolTip(this.PIX_DCO_ALL_chkBox, "If checked, edits to Adj and Ctrl apply to ALL Pixels' DCOs");
		this.PIX_DCO_ALL_chkBox.UseVisualStyleBackColor = true;
		this.MAT_FE_ALL_chkBox.AutoSize = true;
		this.MAT_FE_ALL_chkBox.Location = new System.Drawing.Point(105, 42);
		this.MAT_FE_ALL_chkBox.Name = "MAT_FE_ALL_chkBox";
		this.MAT_FE_ALL_chkBox.Size = new System.Drawing.Size(73, 17);
		this.MAT_FE_ALL_chkBox.TabIndex = 36;
		this.MAT_FE_ALL_chkBox.Text = "Edit all FE";
		this.toolTip1.SetToolTip(this.MAT_FE_ALL_chkBox, "Turn ON All Front Ends at once");
		this.MAT_FE_ALL_chkBox.UseVisualStyleBackColor = true;
		this.MAT_FE_ON_chkBox.AutoSize = true;
		this.MAT_FE_ON_chkBox.Location = new System.Drawing.Point(105, 56);
		this.MAT_FE_ON_chkBox.Name = "MAT_FE_ON_chkBox";
		this.MAT_FE_ON_chkBox.Size = new System.Drawing.Size(61, 17);
		this.MAT_FE_ON_chkBox.TabIndex = 38;
		this.MAT_FE_ON_chkBox.Text = "FE  ON";
		this.toolTip1.SetToolTip(this.MAT_FE_ON_chkBox, "Turn ON Selected Front End");
		this.MAT_FE_ON_chkBox.UseVisualStyleBackColor = true;
		this.MAT_FE_ON_chkBox.CheckedChanged += new System.EventHandler(PIXset_change);
		this.PIX_ON_chkBox.AutoSize = true;
		this.PIX_ON_chkBox.Location = new System.Drawing.Point(4, 56);
		this.PIX_ON_chkBox.Name = "PIX_ON_chkBox";
		this.PIX_ON_chkBox.Size = new System.Drawing.Size(62, 17);
		this.PIX_ON_chkBox.TabIndex = 37;
		this.PIX_ON_chkBox.Text = "PIX ON";
		this.toolTip1.SetToolTip(this.PIX_ON_chkBox, "Turn ON Selected Pixel");
		this.PIX_ON_chkBox.UseVisualStyleBackColor = true;
		this.PIX_ON_chkBox.CheckedChanged += new System.EventHandler(PIXset_change);
		this.CAL_SEL_DCO_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.CAL_SEL_DCO_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.CAL_SEL_DCO_comboBox.FormattingEnabled = true;
		this.CAL_SEL_DCO_comboBox.Items.AddRange(new object[2] { "DCO 0", "DCO 1 Groups" });
		this.CAL_SEL_DCO_comboBox.Location = new System.Drawing.Point(130, 17);
		this.CAL_SEL_DCO_comboBox.MaxDropDownItems = 2;
		this.CAL_SEL_DCO_comboBox.Name = "CAL_SEL_DCO_comboBox";
		this.CAL_SEL_DCO_comboBox.Size = new System.Drawing.Size(95, 21);
		this.CAL_SEL_DCO_comboBox.TabIndex = 34;
		this.toolTip1.SetToolTip(this.CAL_SEL_DCO_comboBox, "TDC Signal Source selector");
		this.CAL_SEL_DCO_comboBox.SelectedIndexChanged += new System.EventHandler(MAT_DCO_COMMAND_but_Click);
		this.MAT_DAC_VTH_H_EN_chkBox.AutoSize = true;
		this.MAT_DAC_VTH_H_EN_chkBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.MAT_DAC_VTH_H_EN_chkBox.Location = new System.Drawing.Point(5, 78);
		this.MAT_DAC_VTH_H_EN_chkBox.Name = "MAT_DAC_VTH_H_EN_chkBox";
		this.MAT_DAC_VTH_H_EN_chkBox.Size = new System.Drawing.Size(116, 17);
		this.MAT_DAC_VTH_H_EN_chkBox.TabIndex = 37;
		this.MAT_DAC_VTH_H_EN_chkBox.Text = "EN VTH High DAC";
		this.toolTip1.SetToolTip(this.MAT_DAC_VTH_H_EN_chkBox, "Turn ON Selected Internal DAC (NO FINE TUNE)");
		this.MAT_DAC_VTH_H_EN_chkBox.UseVisualStyleBackColor = true;
		this.MAT_DAC_VTH_H_EN_chkBox.CheckedChanged += new System.EventHandler(MAT_IN_DACset_change);
		this.MAT_DAC_FT_UpDown.Location = new System.Drawing.Point(79, 56);
		this.MAT_DAC_FT_UpDown.Maximum = new decimal(new int[4] { 15, 0, 0, 0 });
		this.MAT_DAC_FT_UpDown.Name = "MAT_DAC_FT_UpDown";
		this.MAT_DAC_FT_UpDown.Size = new System.Drawing.Size(39, 20);
		this.MAT_DAC_FT_UpDown.TabIndex = 31;
		this.toolTip1.SetToolTip(this.MAT_DAC_FT_UpDown, "Set Phase for TA pulse");
		this.MAT_DAC_FT_UpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.MAT_DAC_FT_UpDown.ValueChanged += new System.EventHandler(MAT_FT_DACset_change);
		this.MAT_DAC_VTH_H_UpDown.Location = new System.Drawing.Point(118, 77);
		this.MAT_DAC_VTH_H_UpDown.Maximum = new decimal(new int[4] { 127, 0, 0, 0 });
		this.MAT_DAC_VTH_H_UpDown.Name = "MAT_DAC_VTH_H_UpDown";
		this.MAT_DAC_VTH_H_UpDown.Size = new System.Drawing.Size(38, 20);
		this.MAT_DAC_VTH_H_UpDown.TabIndex = 30;
		this.toolTip1.SetToolTip(this.MAT_DAC_VTH_H_UpDown, "Set Phase for TA pulse");
		this.MAT_DAC_VTH_H_UpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.MAT_DAC_VTH_H_UpDown.ValueChanged += new System.EventHandler(MAT_IN_DACset_change);
		this.MAT_DAC_ALL_FT_chkBox.AutoSize = true;
		this.MAT_DAC_ALL_FT_chkBox.Location = new System.Drawing.Point(3, 19);
		this.MAT_DAC_ALL_FT_chkBox.Name = "MAT_DAC_ALL_FT_chkBox";
		this.MAT_DAC_ALL_FT_chkBox.Size = new System.Drawing.Size(73, 17);
		this.MAT_DAC_ALL_FT_chkBox.TabIndex = 25;
		this.MAT_DAC_ALL_FT_chkBox.Text = "Edit all FT";
		this.toolTip1.SetToolTip(this.MAT_DAC_ALL_FT_chkBox, "Edit All Internal FINE TUNE DACs ");
		this.MAT_DAC_ALL_FT_chkBox.UseVisualStyleBackColor = true;
		this.MAT_DAC_FT_SEL_UpDown.Location = new System.Drawing.Point(79, 37);
		this.MAT_DAC_FT_SEL_UpDown.Maximum = new decimal(new int[4] { 63, 0, 0, 0 });
		this.MAT_DAC_FT_SEL_UpDown.Name = "MAT_DAC_FT_SEL_UpDown";
		this.MAT_DAC_FT_SEL_UpDown.Size = new System.Drawing.Size(39, 20);
		this.MAT_DAC_FT_SEL_UpDown.TabIndex = 23;
		this.toolTip1.SetToolTip(this.MAT_DAC_FT_SEL_UpDown, "Fine Tune DAC Selector");
		this.MAT_DAC_FT_SEL_UpDown.ValueChanged += new System.EventHandler(MAT_FT_DACsetNum_change);
		this.SEL_VINJ_MUX_High_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.SEL_VINJ_MUX_High_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.SEL_VINJ_MUX_High_comboBox.FormattingEnabled = true;
		this.SEL_VINJ_MUX_High_comboBox.Items.AddRange(new object[2] { "750 mV", "VDDA (900 mV)" });
		this.SEL_VINJ_MUX_High_comboBox.Location = new System.Drawing.Point(110, 98);
		this.SEL_VINJ_MUX_High_comboBox.MaxDropDownItems = 2;
		this.SEL_VINJ_MUX_High_comboBox.Name = "SEL_VINJ_MUX_High_comboBox";
		this.SEL_VINJ_MUX_High_comboBox.Size = new System.Drawing.Size(69, 21);
		this.SEL_VINJ_MUX_High_comboBox.TabIndex = 40;
		this.toolTip1.SetToolTip(this.SEL_VINJ_MUX_High_comboBox, "Select VINJ MUX");
		this.SEL_VINJ_MUX_High_comboBox.SelectedIndexChanged += new System.EventHandler(MAT_AFE_change);
		this.MAT_DAC_VTH_L_UpDown.Location = new System.Drawing.Point(118, 96);
		this.MAT_DAC_VTH_L_UpDown.Maximum = new decimal(new int[4] { 127, 0, 0, 0 });
		this.MAT_DAC_VTH_L_UpDown.Name = "MAT_DAC_VTH_L_UpDown";
		this.MAT_DAC_VTH_L_UpDown.Size = new System.Drawing.Size(38, 20);
		this.MAT_DAC_VTH_L_UpDown.TabIndex = 40;
		this.toolTip1.SetToolTip(this.MAT_DAC_VTH_L_UpDown, "Set Phase for TA pulse");
		this.MAT_DAC_VTH_L_UpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.MAT_DAC_VTH_L_UpDown.ValueChanged += new System.EventHandler(MAT_IN_DACset_change);
		this.MAT_DAC_VTH_L_EN_chkBox.AutoSize = true;
		this.MAT_DAC_VTH_L_EN_chkBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.MAT_DAC_VTH_L_EN_chkBox.Location = new System.Drawing.Point(5, 97);
		this.MAT_DAC_VTH_L_EN_chkBox.Name = "MAT_DAC_VTH_L_EN_chkBox";
		this.MAT_DAC_VTH_L_EN_chkBox.Size = new System.Drawing.Size(114, 17);
		this.MAT_DAC_VTH_L_EN_chkBox.TabIndex = 41;
		this.MAT_DAC_VTH_L_EN_chkBox.Text = "EN VTH Low DAC";
		this.toolTip1.SetToolTip(this.MAT_DAC_VTH_L_EN_chkBox, "Turn ON Selected Internal DAC (NO FINE TUNE)");
		this.MAT_DAC_VTH_L_EN_chkBox.UseVisualStyleBackColor = true;
		this.MAT_DAC_VTH_L_EN_chkBox.CheckedChanged += new System.EventHandler(MAT_IN_DACset_change);
		this.MAT_DAC_VINJ_H_UpDown.Location = new System.Drawing.Point(118, 115);
		this.MAT_DAC_VINJ_H_UpDown.Maximum = new decimal(new int[4] { 127, 0, 0, 0 });
		this.MAT_DAC_VINJ_H_UpDown.Name = "MAT_DAC_VINJ_H_UpDown";
		this.MAT_DAC_VINJ_H_UpDown.Size = new System.Drawing.Size(38, 20);
		this.MAT_DAC_VINJ_H_UpDown.TabIndex = 42;
		this.toolTip1.SetToolTip(this.MAT_DAC_VINJ_H_UpDown, "Set Phase for TA pulse");
		this.MAT_DAC_VINJ_H_UpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.MAT_DAC_VINJ_H_UpDown.ValueChanged += new System.EventHandler(MAT_IN_DACset_change);
		this.MAT_DAC_VINJ_H_EN_chkBox.AutoSize = true;
		this.MAT_DAC_VINJ_H_EN_chkBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.MAT_DAC_VINJ_H_EN_chkBox.Location = new System.Drawing.Point(5, 116);
		this.MAT_DAC_VINJ_H_EN_chkBox.Name = "MAT_DAC_VINJ_H_EN_chkBox";
		this.MAT_DAC_VINJ_H_EN_chkBox.Size = new System.Drawing.Size(117, 17);
		this.MAT_DAC_VINJ_H_EN_chkBox.TabIndex = 43;
		this.MAT_DAC_VINJ_H_EN_chkBox.Text = "EN VINJ High DAC";
		this.toolTip1.SetToolTip(this.MAT_DAC_VINJ_H_EN_chkBox, "Turn ON Selected Internal DAC (NO FINE TUNE)");
		this.MAT_DAC_VINJ_H_EN_chkBox.UseVisualStyleBackColor = true;
		this.MAT_DAC_VINJ_H_EN_chkBox.CheckedChanged += new System.EventHandler(MAT_IN_DACset_change);
		this.MAT_DAC_VINJ_L_UpDown.Location = new System.Drawing.Point(118, 134);
		this.MAT_DAC_VINJ_L_UpDown.Maximum = new decimal(new int[4] { 127, 0, 0, 0 });
		this.MAT_DAC_VINJ_L_UpDown.Name = "MAT_DAC_VINJ_L_UpDown";
		this.MAT_DAC_VINJ_L_UpDown.Size = new System.Drawing.Size(38, 20);
		this.MAT_DAC_VINJ_L_UpDown.TabIndex = 44;
		this.toolTip1.SetToolTip(this.MAT_DAC_VINJ_L_UpDown, "Set Phase for TA pulse");
		this.MAT_DAC_VINJ_L_UpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.MAT_DAC_VINJ_L_UpDown.ValueChanged += new System.EventHandler(MAT_IN_DACset_change);
		this.MAT_DAC_VINJ_L_EN_chkBox.AutoSize = true;
		this.MAT_DAC_VINJ_L_EN_chkBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.MAT_DAC_VINJ_L_EN_chkBox.Location = new System.Drawing.Point(5, 135);
		this.MAT_DAC_VINJ_L_EN_chkBox.Name = "MAT_DAC_VINJ_L_EN_chkBox";
		this.MAT_DAC_VINJ_L_EN_chkBox.Size = new System.Drawing.Size(115, 17);
		this.MAT_DAC_VINJ_L_EN_chkBox.TabIndex = 45;
		this.MAT_DAC_VINJ_L_EN_chkBox.Text = "EN VINJ Low DAC";
		this.toolTip1.SetToolTip(this.MAT_DAC_VINJ_L_EN_chkBox, "Turn ON Selected Internal DAC (NO FINE TUNE)");
		this.MAT_DAC_VINJ_L_EN_chkBox.UseVisualStyleBackColor = true;
		this.MAT_DAC_VINJ_L_EN_chkBox.CheckedChanged += new System.EventHandler(MAT_IN_DACset_change);
		this.MAT_DAC_VLDO_UpDown.Location = new System.Drawing.Point(118, 153);
		this.MAT_DAC_VLDO_UpDown.Maximum = new decimal(new int[4] { 127, 0, 0, 0 });
		this.MAT_DAC_VLDO_UpDown.Name = "MAT_DAC_VLDO_UpDown";
		this.MAT_DAC_VLDO_UpDown.Size = new System.Drawing.Size(38, 20);
		this.MAT_DAC_VLDO_UpDown.TabIndex = 46;
		this.toolTip1.SetToolTip(this.MAT_DAC_VLDO_UpDown, "Set Phase for TA pulse");
		this.MAT_DAC_VLDO_UpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.MAT_DAC_VLDO_UpDown.ValueChanged += new System.EventHandler(MAT_IN_DACset_change);
		this.MAT_DAC_VLDO_EN_chkBox.AutoSize = true;
		this.MAT_DAC_VLDO_EN_chkBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.MAT_DAC_VLDO_EN_chkBox.Location = new System.Drawing.Point(5, 154);
		this.MAT_DAC_VLDO_EN_chkBox.Name = "MAT_DAC_VLDO_EN_chkBox";
		this.MAT_DAC_VLDO_EN_chkBox.Size = new System.Drawing.Size(98, 17);
		this.MAT_DAC_VLDO_EN_chkBox.TabIndex = 47;
		this.MAT_DAC_VLDO_EN_chkBox.Text = "EN VLDO DAC";
		this.toolTip1.SetToolTip(this.MAT_DAC_VLDO_EN_chkBox, "Turn ON Selected Internal DAC (NO FINE TUNE)");
		this.MAT_DAC_VLDO_EN_chkBox.UseVisualStyleBackColor = true;
		this.MAT_DAC_VLDO_EN_chkBox.CheckedChanged += new System.EventHandler(MAT_IN_DACset_change);
		this.MAT_DAC_VFB_UpDown.Location = new System.Drawing.Point(118, 172);
		this.MAT_DAC_VFB_UpDown.Maximum = new decimal(new int[4] { 127, 0, 0, 0 });
		this.MAT_DAC_VFB_UpDown.Name = "MAT_DAC_VFB_UpDown";
		this.MAT_DAC_VFB_UpDown.Size = new System.Drawing.Size(38, 20);
		this.MAT_DAC_VFB_UpDown.TabIndex = 48;
		this.toolTip1.SetToolTip(this.MAT_DAC_VFB_UpDown, "Set Phase for TA pulse");
		this.MAT_DAC_VFB_UpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.MAT_DAC_VFB_UpDown.ValueChanged += new System.EventHandler(MAT_IN_DACset_change);
		this.MAT_DAC_VFB_EN_chkBox.AutoSize = true;
		this.MAT_DAC_VFB_EN_chkBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.MAT_DAC_VFB_EN_chkBox.Location = new System.Drawing.Point(5, 173);
		this.MAT_DAC_VFB_EN_chkBox.Name = "MAT_DAC_VFB_EN_chkBox";
		this.MAT_DAC_VFB_EN_chkBox.Size = new System.Drawing.Size(89, 17);
		this.MAT_DAC_VFB_EN_chkBox.TabIndex = 49;
		this.MAT_DAC_VFB_EN_chkBox.Text = "EN VFB DAC";
		this.toolTip1.SetToolTip(this.MAT_DAC_VFB_EN_chkBox, "Turn ON Selected Internal DAC (NO FINE TUNE)");
		this.MAT_DAC_VFB_EN_chkBox.UseVisualStyleBackColor = true;
		this.MAT_DAC_VFB_EN_chkBox.CheckedChanged += new System.EventHandler(MAT_IN_DACset_change);
		this.DAC_IDISC_UpDown.Location = new System.Drawing.Point(118, 191);
		this.DAC_IDISC_UpDown.Maximum = new decimal(new int[4] { 7, 0, 0, 0 });
		this.DAC_IDISC_UpDown.Name = "DAC_IDISC_UpDown";
		this.DAC_IDISC_UpDown.Size = new System.Drawing.Size(38, 20);
		this.DAC_IDISC_UpDown.TabIndex = 50;
		this.toolTip1.SetToolTip(this.DAC_IDISC_UpDown, "Discriminator Current");
		this.DAC_IDISC_UpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.DAC_IDISC_UpDown.ValueChanged += new System.EventHandler(MAT_AFE_change);
		this.DAC_ICSA_UpDown.Location = new System.Drawing.Point(118, 210);
		this.DAC_ICSA_UpDown.Maximum = new decimal(new int[4] { 7, 0, 0, 0 });
		this.DAC_ICSA_UpDown.Name = "DAC_ICSA_UpDown";
		this.DAC_ICSA_UpDown.Size = new System.Drawing.Size(38, 20);
		this.DAC_ICSA_UpDown.TabIndex = 51;
		this.toolTip1.SetToolTip(this.DAC_ICSA_UpDown, "Charge-Sensitive Amp. Current");
		this.DAC_ICSA_UpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.DAC_ICSA_UpDown.ValueChanged += new System.EventHandler(MAT_AFE_change);
		this.DAC_IKRUM_UpDown.Location = new System.Drawing.Point(118, 229);
		this.DAC_IKRUM_UpDown.Maximum = new decimal(new int[4] { 15, 0, 0, 0 });
		this.DAC_IKRUM_UpDown.Name = "DAC_IKRUM_UpDown";
		this.DAC_IKRUM_UpDown.Size = new System.Drawing.Size(38, 20);
		this.DAC_IKRUM_UpDown.TabIndex = 52;
		this.toolTip1.SetToolTip(this.DAC_IKRUM_UpDown, "Krumenacher Current");
		this.DAC_IKRUM_UpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.DAC_IKRUM_UpDown.ValueChanged += new System.EventHandler(MAT_AFE_change);
		this.CH_MODE_42_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.CH_MODE_42_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.CH_MODE_42_comboBox.FormattingEnabled = true;
		this.CH_MODE_42_comboBox.Items.AddRange(new object[4] { "DAQ TMR GPO", "DAQ HIT-OR", "TDC Pulse", "ATP Pulse" });
		this.CH_MODE_42_comboBox.Location = new System.Drawing.Point(48, 136);
		this.CH_MODE_42_comboBox.MaxDropDownItems = 4;
		this.CH_MODE_42_comboBox.Name = "CH_MODE_42_comboBox";
		this.CH_MODE_42_comboBox.Size = new System.Drawing.Size(90, 21);
		this.CH_MODE_42_comboBox.TabIndex = 41;
		this.toolTip1.SetToolTip(this.CH_MODE_42_comboBox, "Select Test Mode");
		this.CH_MODE_42_comboBox.SelectedIndexChanged += new System.EventHandler(config_mode_change);
		this.CH_MODE_41_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.CH_MODE_41_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.CH_MODE_41_comboBox.FormattingEnabled = true;
		this.CH_MODE_41_comboBox.Items.AddRange(new object[4] { "DAQ TMR GPO", "DAQ HIT-OR", "TDC Pulse", "ATP Pulse" });
		this.CH_MODE_41_comboBox.Location = new System.Drawing.Point(48, 115);
		this.CH_MODE_41_comboBox.MaxDropDownItems = 4;
		this.CH_MODE_41_comboBox.Name = "CH_MODE_41_comboBox";
		this.CH_MODE_41_comboBox.Size = new System.Drawing.Size(90, 21);
		this.CH_MODE_41_comboBox.TabIndex = 42;
		this.toolTip1.SetToolTip(this.CH_MODE_41_comboBox, "Select Test Mode");
		this.CH_MODE_41_comboBox.SelectedIndexChanged += new System.EventHandler(config_mode_change);
		this.CH_SEL_42_UpDown.Location = new System.Drawing.Point(142, 138);
		this.CH_SEL_42_UpDown.Maximum = new decimal(new int[4] { 63, 0, 0, 0 });
		this.CH_SEL_42_UpDown.Name = "CH_SEL_42_UpDown";
		this.CH_SEL_42_UpDown.Size = new System.Drawing.Size(39, 20);
		this.CH_SEL_42_UpDown.TabIndex = 43;
		this.toolTip1.SetToolTip(this.CH_SEL_42_UpDown, "Channel Selector for TDC/ATP Pulse Mode");
		this.CH_SEL_42_UpDown.ValueChanged += new System.EventHandler(config_mode_change);
		this.CH_SEL_41_UpDown.Location = new System.Drawing.Point(142, 116);
		this.CH_SEL_41_UpDown.Maximum = new decimal(new int[4] { 63, 0, 0, 0 });
		this.CH_SEL_41_UpDown.Name = "CH_SEL_41_UpDown";
		this.CH_SEL_41_UpDown.Size = new System.Drawing.Size(39, 20);
		this.CH_SEL_41_UpDown.TabIndex = 44;
		this.toolTip1.SetToolTip(this.CH_SEL_41_UpDown, "Channel Selector for TDC/ATP Pulse Mode");
		this.CH_SEL_41_UpDown.ValueChanged += new System.EventHandler(config_mode_change);
		this.SEL_VINJ_MUX_Low_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.SEL_VINJ_MUX_Low_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.SEL_VINJ_MUX_Low_comboBox.FormattingEnabled = true;
		this.SEL_VINJ_MUX_Low_comboBox.Items.AddRange(new object[2] { "150 mV", "GND (0 mV)" });
		this.SEL_VINJ_MUX_Low_comboBox.Location = new System.Drawing.Point(110, 118);
		this.SEL_VINJ_MUX_Low_comboBox.MaxDropDownItems = 2;
		this.SEL_VINJ_MUX_Low_comboBox.Name = "SEL_VINJ_MUX_Low_comboBox";
		this.SEL_VINJ_MUX_Low_comboBox.Size = new System.Drawing.Size(69, 21);
		this.SEL_VINJ_MUX_Low_comboBox.TabIndex = 46;
		this.toolTip1.SetToolTip(this.SEL_VINJ_MUX_Low_comboBox, "Select VINJ MUX");
		this.SEL_VINJ_MUX_Low_comboBox.SelectedIndexChanged += new System.EventHandler(MAT_AFE_change);
		this.openFileDialog1.FileName = "openFileDialog1";
		this.exAdcDac_tabPage.BackColor = System.Drawing.Color.LightGray;
		this.exAdcDac_tabPage.Controls.Add(this.Quad_DAC_grpBox);
		this.exAdcDac_tabPage.Controls.Add(this.groupBox1);
		this.exAdcDac_tabPage.Controls.Add(this.ExtSelQuad_SE_chkBox);
		this.exAdcDac_tabPage.Controls.Add(this.ExtSelQuad_NE_chkBox);
		this.exAdcDac_tabPage.Controls.Add(this.ExtSelQuad_SW_chkBox);
		this.exAdcDac_tabPage.Controls.Add(this.ExtSelQuad_NW_chkBox);
		this.exAdcDac_tabPage.Controls.Add(this.Quad_ADC_grpBox);
		this.exAdcDac_tabPage.Controls.Add(this.CurMonADC_groupBox);
		this.exAdcDac_tabPage.Controls.Add(this.ExtDAC_groupBox);
		this.exAdcDac_tabPage.Location = new System.Drawing.Point(4, 25);
		this.exAdcDac_tabPage.Name = "exAdcDac_tabPage";
		this.exAdcDac_tabPage.Size = new System.Drawing.Size(942, 410);
		this.exAdcDac_tabPage.TabIndex = 3;
		this.exAdcDac_tabPage.Text = "Ext ADC / DAC";
		this.Quad_DAC_grpBox.BackColor = System.Drawing.Color.LightGreen;
		this.Quad_DAC_grpBox.Controls.Add(this.Ext_Icap_label);
		this.Quad_DAC_grpBox.Controls.Add(this.DACicap_UpDown);
		this.Quad_DAC_grpBox.Controls.Add(this.DACicap_but);
		this.Quad_DAC_grpBox.Controls.Add(this.Ext_Iref_label);
		this.Quad_DAC_grpBox.Controls.Add(this.DACiref_UpDown);
		this.Quad_DAC_grpBox.Controls.Add(this.DACiref_but);
		this.Quad_DAC_grpBox.Controls.Add(this.Ext_Vfeed_label);
		this.Quad_DAC_grpBox.Controls.Add(this.textBox1);
		this.Quad_DAC_grpBox.Controls.Add(this.DACvfeed_UpDown);
		this.Quad_DAC_grpBox.Controls.Add(this.DACvfeed_but);
		this.Quad_DAC_grpBox.Controls.Add(this.Ext_Quad_VDDA_label);
		this.Quad_DAC_grpBox.Location = new System.Drawing.Point(283, 167);
		this.Quad_DAC_grpBox.Name = "Quad_DAC_grpBox";
		this.Quad_DAC_grpBox.Size = new System.Drawing.Size(234, 131);
		this.Quad_DAC_grpBox.TabIndex = 33;
		this.Quad_DAC_grpBox.TabStop = false;
		this.Quad_DAC_grpBox.Text = "External Quadrant DACs";
		this.Ext_Icap_label.AutoSize = true;
		this.Ext_Icap_label.Location = new System.Drawing.Point(85, 101);
		this.Ext_Icap_label.Name = "Ext_Icap_label";
		this.Ext_Icap_label.Size = new System.Drawing.Size(52, 13);
		this.Ext_Icap_label.TabIndex = 26;
		this.Ext_Icap_label.Text = "Icap (mV)";
		this.DACicap_UpDown.DecimalPlaces = 2;
		this.DACicap_UpDown.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.DACicap_UpDown.Location = new System.Drawing.Point(13, 97);
		this.DACicap_UpDown.Maximum = new decimal(new int[4] { 1200, 0, 0, 0 });
		this.DACicap_UpDown.Name = "DACicap_UpDown";
		this.DACicap_UpDown.Size = new System.Drawing.Size(71, 20);
		this.DACicap_UpDown.TabIndex = 24;
		this.DACicap_UpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.DACicap_UpDown.Value = new decimal(new int[4] { 900, 0, 0, 0 });
		this.DACicap_but.Location = new System.Drawing.Point(149, 96);
		this.DACicap_but.Name = "DACicap_but";
		this.DACicap_but.Size = new System.Drawing.Size(74, 23);
		this.DACicap_but.TabIndex = 25;
		this.DACicap_but.Text = "Write Icap";
		this.DACicap_but.UseVisualStyleBackColor = true;
		this.DACicap_but.Click += new System.EventHandler(WriteDac_Click);
		this.Ext_Iref_label.AutoSize = true;
		this.Ext_Iref_label.Location = new System.Drawing.Point(85, 75);
		this.Ext_Iref_label.Name = "Ext_Iref_label";
		this.Ext_Iref_label.Size = new System.Drawing.Size(46, 13);
		this.Ext_Iref_label.TabIndex = 23;
		this.Ext_Iref_label.Text = "Iref (mV)";
		this.DACiref_UpDown.DecimalPlaces = 2;
		this.DACiref_UpDown.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.DACiref_UpDown.Location = new System.Drawing.Point(13, 71);
		this.DACiref_UpDown.Maximum = new decimal(new int[4] { 1200, 0, 0, 0 });
		this.DACiref_UpDown.Name = "DACiref_UpDown";
		this.DACiref_UpDown.Size = new System.Drawing.Size(71, 20);
		this.DACiref_UpDown.TabIndex = 21;
		this.DACiref_UpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.DACiref_UpDown.Value = new decimal(new int[4] { 900, 0, 0, 0 });
		this.DACiref_but.Location = new System.Drawing.Point(149, 70);
		this.DACiref_but.Name = "DACiref_but";
		this.DACiref_but.Size = new System.Drawing.Size(74, 23);
		this.DACiref_but.TabIndex = 22;
		this.DACiref_but.Text = "Write Iref";
		this.DACiref_but.UseVisualStyleBackColor = true;
		this.DACiref_but.Click += new System.EventHandler(WriteDac_Click);
		this.Ext_Vfeed_label.AutoSize = true;
		this.Ext_Vfeed_label.Location = new System.Drawing.Point(85, 49);
		this.Ext_Vfeed_label.Name = "Ext_Vfeed_label";
		this.Ext_Vfeed_label.Size = new System.Drawing.Size(59, 13);
		this.Ext_Vfeed_label.TabIndex = 18;
		this.Ext_Vfeed_label.Text = "Vfeed (mV)";
		this.textBox1.Location = new System.Drawing.Point(13, 19);
		this.textBox1.Name = "textBox1";
		this.textBox1.Size = new System.Drawing.Size(48, 20);
		this.textBox1.TabIndex = 19;
		this.textBox1.Text = "900";
		this.textBox1.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.DACvfeed_UpDown.DecimalPlaces = 2;
		this.DACvfeed_UpDown.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.DACvfeed_UpDown.Location = new System.Drawing.Point(13, 45);
		this.DACvfeed_UpDown.Maximum = new decimal(new int[4] { 1200, 0, 0, 0 });
		this.DACvfeed_UpDown.Name = "DACvfeed_UpDown";
		this.DACvfeed_UpDown.Size = new System.Drawing.Size(71, 20);
		this.DACvfeed_UpDown.TabIndex = 16;
		this.DACvfeed_UpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.DACvfeed_UpDown.Value = new decimal(new int[4] { 900, 0, 0, 0 });
		this.DACvfeed_but.Location = new System.Drawing.Point(149, 44);
		this.DACvfeed_but.Name = "DACvfeed_but";
		this.DACvfeed_but.Size = new System.Drawing.Size(74, 23);
		this.DACvfeed_but.TabIndex = 17;
		this.DACvfeed_but.Text = "Write Vfeed";
		this.DACvfeed_but.UseVisualStyleBackColor = true;
		this.DACvfeed_but.Click += new System.EventHandler(WriteDac_Click);
		this.Ext_Quad_VDDA_label.AutoSize = true;
		this.Ext_Quad_VDDA_label.Location = new System.Drawing.Point(67, 26);
		this.Ext_Quad_VDDA_label.Name = "Ext_Quad_VDDA_label";
		this.Ext_Quad_VDDA_label.Size = new System.Drawing.Size(61, 13);
		this.Ext_Quad_VDDA_label.TabIndex = 20;
		this.Ext_Quad_VDDA_label.Text = "VDDA (mV)";
		this.groupBox1.Controls.Add(this.groupBox2);
		this.groupBox1.Controls.Add(this.groupBox4);
		this.groupBox1.Location = new System.Drawing.Point(606, 23);
		this.groupBox1.Name = "groupBox1";
		this.groupBox1.Size = new System.Drawing.Size(316, 158);
		this.groupBox1.TabIndex = 32;
		this.groupBox1.TabStop = false;
		this.groupBox1.Text = "groupBox1";
		this.groupBox2.BackColor = System.Drawing.Color.LightCyan;
		this.groupBox2.Controls.Add(this.Ext_ThresholdScan_label);
		this.groupBox2.Controls.Add(this.ScanVthStep_UpDown);
		this.groupBox2.Controls.Add(this.ScanVthMin_UpDown);
		this.groupBox2.Controls.Add(this.ScanVthMax_UpDown);
		this.groupBox2.Location = new System.Drawing.Point(9, 19);
		this.groupBox2.Name = "groupBox2";
		this.groupBox2.Size = new System.Drawing.Size(141, 124);
		this.groupBox2.TabIndex = 11;
		this.groupBox2.TabStop = false;
		this.groupBox2.Text = "Thresholds Scan";
		this.Ext_ThresholdScan_label.AutoSize = true;
		this.Ext_ThresholdScan_label.Location = new System.Drawing.Point(83, 44);
		this.Ext_ThresholdScan_label.Name = "Ext_ThresholdScan_label";
		this.Ext_ThresholdScan_label.Size = new System.Drawing.Size(53, 65);
		this.Ext_ThresholdScan_label.TabIndex = 12;
		this.Ext_ThresholdScan_label.Text = "Max (mV)\r\n\r\nMin (mV)\r\n\r\nStep (mV)\r\n";
		this.ScanVthStep_UpDown.DecimalPlaces = 2;
		this.ScanVthStep_UpDown.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.ScanVthStep_UpDown.Location = new System.Drawing.Point(6, 93);
		this.ScanVthStep_UpDown.Maximum = new decimal(new int[4] { 50, 0, 0, 0 });
		this.ScanVthStep_UpDown.Name = "ScanVthStep_UpDown";
		this.ScanVthStep_UpDown.Size = new System.Drawing.Size(71, 20);
		this.ScanVthStep_UpDown.TabIndex = 5;
		this.ScanVthStep_UpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.ScanVthStep_UpDown.Value = new decimal(new int[4] { 10, 0, 0, 0 });
		this.ScanVthMin_UpDown.DecimalPlaces = 2;
		this.ScanVthMin_UpDown.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.ScanVthMin_UpDown.Location = new System.Drawing.Point(6, 67);
		this.ScanVthMin_UpDown.Maximum = new decimal(new int[4] { 899, 0, 0, 0 });
		this.ScanVthMin_UpDown.Name = "ScanVthMin_UpDown";
		this.ScanVthMin_UpDown.Size = new System.Drawing.Size(71, 20);
		this.ScanVthMin_UpDown.TabIndex = 4;
		this.ScanVthMin_UpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.ScanVthMin_UpDown.Value = new decimal(new int[4] { 300, 0, 0, 0 });
		this.ScanVthMax_UpDown.DecimalPlaces = 2;
		this.ScanVthMax_UpDown.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.ScanVthMax_UpDown.Location = new System.Drawing.Point(6, 41);
		this.ScanVthMax_UpDown.Maximum = new decimal(new int[4] { 899, 0, 0, 0 });
		this.ScanVthMax_UpDown.Name = "ScanVthMax_UpDown";
		this.ScanVthMax_UpDown.Size = new System.Drawing.Size(71, 20);
		this.ScanVthMax_UpDown.TabIndex = 3;
		this.ScanVthMax_UpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.ScanVthMax_UpDown.Value = new decimal(new int[4] { 650, 0, 0, 0 });
		this.groupBox4.BackColor = System.Drawing.Color.LightCyan;
		this.groupBox4.Controls.Add(this.Ext_Vinj2Scan_label);
		this.groupBox4.Controls.Add(this.ScanVinj2Step_UpDown);
		this.groupBox4.Controls.Add(this.ScanVinj2Min_UpDown);
		this.groupBox4.Controls.Add(this.ScanVinj2Max_UpDown);
		this.groupBox4.Location = new System.Drawing.Point(156, 19);
		this.groupBox4.Name = "groupBox4";
		this.groupBox4.Size = new System.Drawing.Size(141, 124);
		this.groupBox4.TabIndex = 12;
		this.groupBox4.TabStop = false;
		this.groupBox4.Text = "Vinj2 Scan";
		this.Ext_Vinj2Scan_label.AutoSize = true;
		this.Ext_Vinj2Scan_label.Location = new System.Drawing.Point(83, 44);
		this.Ext_Vinj2Scan_label.Name = "Ext_Vinj2Scan_label";
		this.Ext_Vinj2Scan_label.Size = new System.Drawing.Size(53, 65);
		this.Ext_Vinj2Scan_label.TabIndex = 12;
		this.Ext_Vinj2Scan_label.Text = "Max (mV)\r\n\r\nMin (mV)\r\n\r\nStep (mV)\r\n";
		this.ScanVinj2Step_UpDown.DecimalPlaces = 2;
		this.ScanVinj2Step_UpDown.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.ScanVinj2Step_UpDown.Location = new System.Drawing.Point(6, 93);
		this.ScanVinj2Step_UpDown.Maximum = new decimal(new int[4] { 50, 0, 0, 0 });
		this.ScanVinj2Step_UpDown.Name = "ScanVinj2Step_UpDown";
		this.ScanVinj2Step_UpDown.Size = new System.Drawing.Size(71, 20);
		this.ScanVinj2Step_UpDown.TabIndex = 5;
		this.ScanVinj2Step_UpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.ScanVinj2Step_UpDown.Value = new decimal(new int[4] { 10, 0, 0, 0 });
		this.ScanVinj2Min_UpDown.DecimalPlaces = 2;
		this.ScanVinj2Min_UpDown.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.ScanVinj2Min_UpDown.Location = new System.Drawing.Point(6, 67);
		this.ScanVinj2Min_UpDown.Maximum = new decimal(new int[4] { 899, 0, 0, 0 });
		this.ScanVinj2Min_UpDown.Name = "ScanVinj2Min_UpDown";
		this.ScanVinj2Min_UpDown.Size = new System.Drawing.Size(71, 20);
		this.ScanVinj2Min_UpDown.TabIndex = 4;
		this.ScanVinj2Min_UpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.ScanVinj2Min_UpDown.Value = new decimal(new int[4] { 300, 0, 0, 0 });
		this.ScanVinj2Max_UpDown.DecimalPlaces = 2;
		this.ScanVinj2Max_UpDown.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.ScanVinj2Max_UpDown.Location = new System.Drawing.Point(6, 41);
		this.ScanVinj2Max_UpDown.Maximum = new decimal(new int[4] { 1000, 0, 0, 0 });
		this.ScanVinj2Max_UpDown.Name = "ScanVinj2Max_UpDown";
		this.ScanVinj2Max_UpDown.Size = new System.Drawing.Size(71, 20);
		this.ScanVinj2Max_UpDown.TabIndex = 3;
		this.ScanVinj2Max_UpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.ScanVinj2Max_UpDown.Value = new decimal(new int[4] { 650, 0, 0, 0 });
		this.ExtSelQuad_SE_chkBox.AutoSize = true;
		this.ExtSelQuad_SE_chkBox.Location = new System.Drawing.Point(51, 390);
		this.ExtSelQuad_SE_chkBox.Name = "ExtSelQuad_SE_chkBox";
		this.ExtSelQuad_SE_chkBox.Size = new System.Drawing.Size(40, 17);
		this.ExtSelQuad_SE_chkBox.TabIndex = 31;
		this.ExtSelQuad_SE_chkBox.Text = "SE";
		this.ExtSelQuad_SE_chkBox.UseVisualStyleBackColor = true;
		this.ExtSelQuad_SE_chkBox.CheckedChanged += new System.EventHandler(Ext_Quad_Sel_Change);
		this.ExtSelQuad_NE_chkBox.AutoSize = true;
		this.ExtSelQuad_NE_chkBox.Location = new System.Drawing.Point(51, 368);
		this.ExtSelQuad_NE_chkBox.Name = "ExtSelQuad_NE_chkBox";
		this.ExtSelQuad_NE_chkBox.Size = new System.Drawing.Size(41, 17);
		this.ExtSelQuad_NE_chkBox.TabIndex = 30;
		this.ExtSelQuad_NE_chkBox.Text = "NE";
		this.ExtSelQuad_NE_chkBox.UseVisualStyleBackColor = true;
		this.ExtSelQuad_NE_chkBox.CheckedChanged += new System.EventHandler(Ext_Quad_Sel_Change);
		this.ExtSelQuad_SW_chkBox.AutoSize = true;
		this.ExtSelQuad_SW_chkBox.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.ExtSelQuad_SW_chkBox.Location = new System.Drawing.Point(4, 390);
		this.ExtSelQuad_SW_chkBox.Name = "ExtSelQuad_SW_chkBox";
		this.ExtSelQuad_SW_chkBox.Size = new System.Drawing.Size(44, 17);
		this.ExtSelQuad_SW_chkBox.TabIndex = 29;
		this.ExtSelQuad_SW_chkBox.Text = "SW";
		this.ExtSelQuad_SW_chkBox.UseVisualStyleBackColor = true;
		this.ExtSelQuad_SW_chkBox.CheckedChanged += new System.EventHandler(Ext_Quad_Sel_Change);
		this.ExtSelQuad_NW_chkBox.AutoSize = true;
		this.ExtSelQuad_NW_chkBox.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.ExtSelQuad_NW_chkBox.Location = new System.Drawing.Point(4, 368);
		this.ExtSelQuad_NW_chkBox.Name = "ExtSelQuad_NW_chkBox";
		this.ExtSelQuad_NW_chkBox.Size = new System.Drawing.Size(45, 17);
		this.ExtSelQuad_NW_chkBox.TabIndex = 28;
		this.ExtSelQuad_NW_chkBox.Text = "NW";
		this.ExtSelQuad_NW_chkBox.UseVisualStyleBackColor = true;
		this.ExtSelQuad_NW_chkBox.CheckedChanged += new System.EventHandler(Ext_Quad_Sel_Change);
		this.Quad_ADC_grpBox.BackColor = System.Drawing.Color.LightGreen;
		this.Quad_ADC_grpBox.Controls.Add(this.ADCdac_Quad_NShot_label);
		this.Quad_ADC_grpBox.Controls.Add(this.ADCdac_Quad_NShot_button);
		this.Quad_ADC_grpBox.Controls.Add(this.ADCdac_Quad_NShot_numUpDown);
		this.Quad_ADC_grpBox.Controls.Add(this.ADCdac_Quad_Value_tBox);
		this.Quad_ADC_grpBox.Controls.Add(this.Ext_QuadADC_Config_label);
		this.Quad_ADC_grpBox.Controls.Add(this.ADCdac_Quad_Config_tBox);
		this.Quad_ADC_grpBox.Controls.Add(this.ADCdac_Quad_DataDec_tBox);
		this.Quad_ADC_grpBox.Controls.Add(this.Ext_QuadADC_Data_label);
		this.Quad_ADC_grpBox.Controls.Add(this.ADCdac_Quad_DataHex_tBox);
		this.Quad_ADC_grpBox.Controls.Add(this.ADCdac_Quad_Read_but);
		this.Quad_ADC_grpBox.Controls.Add(this.ADCdac_Quad_1Shot_but);
		this.Quad_ADC_grpBox.Controls.Add(this.ADCdac_Quad_Write_but);
		this.Quad_ADC_grpBox.Controls.Add(this.Ext_QuadADC_ResGain_label);
		this.Quad_ADC_grpBox.Controls.Add(this.Ext_QuadADC_Val_Monitored_label);
		this.Quad_ADC_grpBox.Controls.Add(this.ADCdac_Quad_Gain_comboBox);
		this.Quad_ADC_grpBox.Controls.Add(this.ADCdac_Quad_Res_comboBox);
		this.Quad_ADC_grpBox.Controls.Add(this.ADCdac_Quad_ch_comboBox);
		this.Quad_ADC_grpBox.Controls.Add(this.ADCdac_Quad_RDY_chkBox);
		this.Quad_ADC_grpBox.Controls.Add(this.ADCdac_Quad_OC_chkBox);
		this.Quad_ADC_grpBox.Controls.Add(this.Ext_QuadADC_HexDecV_label);
		this.Quad_ADC_grpBox.Location = new System.Drawing.Point(279, 6);
		this.Quad_ADC_grpBox.Name = "Quad_ADC_grpBox";
		this.Quad_ADC_grpBox.Size = new System.Drawing.Size(238, 157);
		this.Quad_ADC_grpBox.TabIndex = 15;
		this.Quad_ADC_grpBox.TabStop = false;
		this.Quad_ADC_grpBox.Text = "Quadrant Monitor ADC";
		this.ADCdac_Quad_NShot_label.AutoSize = true;
		this.ADCdac_Quad_NShot_label.Location = new System.Drawing.Point(6, 98);
		this.ADCdac_Quad_NShot_label.Name = "ADCdac_Quad_NShot_label";
		this.ADCdac_Quad_NShot_label.Size = new System.Drawing.Size(15, 13);
		this.ADCdac_Quad_NShot_label.TabIndex = 22;
		this.ADCdac_Quad_NShot_label.Text = "N";
		this.ADCdac_Quad_NShot_button.Location = new System.Drawing.Point(80, 94);
		this.ADCdac_Quad_NShot_button.Name = "ADCdac_Quad_NShot_button";
		this.ADCdac_Quad_NShot_button.Size = new System.Drawing.Size(75, 23);
		this.ADCdac_Quad_NShot_button.TabIndex = 21;
		this.ADCdac_Quad_NShot_button.Text = "N Shot Avg";
		this.ADCdac_Quad_NShot_button.UseVisualStyleBackColor = true;
		this.ADCdac_Quad_NShot_button.Click += new System.EventHandler(NShotAdc_Click);
		this.ADCdac_Quad_NShot_numUpDown.Location = new System.Drawing.Point(27, 95);
		this.ADCdac_Quad_NShot_numUpDown.Maximum = new decimal(new int[4] { 3000, 0, 0, 0 });
		this.ADCdac_Quad_NShot_numUpDown.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.ADCdac_Quad_NShot_numUpDown.Name = "ADCdac_Quad_NShot_numUpDown";
		this.ADCdac_Quad_NShot_numUpDown.Size = new System.Drawing.Size(44, 20);
		this.ADCdac_Quad_NShot_numUpDown.TabIndex = 20;
		this.ADCdac_Quad_NShot_numUpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.ADCdac_Quad_Value_tBox.Location = new System.Drawing.Point(107, 129);
		this.ADCdac_Quad_Value_tBox.Name = "ADCdac_Quad_Value_tBox";
		this.ADCdac_Quad_Value_tBox.ReadOnly = true;
		this.ADCdac_Quad_Value_tBox.Size = new System.Drawing.Size(53, 20);
		this.ADCdac_Quad_Value_tBox.TabIndex = 17;
		this.ADCdac_Quad_Value_tBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.Ext_QuadADC_Config_label.AutoSize = true;
		this.Ext_QuadADC_Config_label.Location = new System.Drawing.Point(166, 124);
		this.Ext_QuadADC_Config_label.Name = "Ext_QuadADC_Config_label";
		this.Ext_QuadADC_Config_label.Size = new System.Drawing.Size(36, 26);
		this.Ext_QuadADC_Config_label.TabIndex = 14;
		this.Ext_QuadADC_Config_label.Text = "ADC\r\nconfig";
		this.ADCdac_Quad_Config_tBox.Location = new System.Drawing.Point(203, 129);
		this.ADCdac_Quad_Config_tBox.Name = "ADCdac_Quad_Config_tBox";
		this.ADCdac_Quad_Config_tBox.Size = new System.Drawing.Size(20, 20);
		this.ADCdac_Quad_Config_tBox.TabIndex = 13;
		this.ADCdac_Quad_Config_tBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
		this.ADCdac_Quad_DataDec_tBox.Location = new System.Drawing.Point(68, 129);
		this.ADCdac_Quad_DataDec_tBox.Name = "ADCdac_Quad_DataDec_tBox";
		this.ADCdac_Quad_DataDec_tBox.ReadOnly = true;
		this.ADCdac_Quad_DataDec_tBox.Size = new System.Drawing.Size(39, 20);
		this.ADCdac_Quad_DataDec_tBox.TabIndex = 12;
		this.ADCdac_Quad_DataDec_tBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.Ext_QuadADC_Data_label.AutoSize = true;
		this.Ext_QuadADC_Data_label.Location = new System.Drawing.Point(3, 124);
		this.Ext_QuadADC_Data_label.Name = "Ext_QuadADC_Data_label";
		this.Ext_QuadADC_Data_label.Size = new System.Drawing.Size(29, 26);
		this.Ext_QuadADC_Data_label.TabIndex = 11;
		this.Ext_QuadADC_Data_label.Text = "ADC\r\ndata";
		this.ADCdac_Quad_DataHex_tBox.Location = new System.Drawing.Point(34, 129);
		this.ADCdac_Quad_DataHex_tBox.Name = "ADCdac_Quad_DataHex_tBox";
		this.ADCdac_Quad_DataHex_tBox.ReadOnly = true;
		this.ADCdac_Quad_DataHex_tBox.Size = new System.Drawing.Size(32, 20);
		this.ADCdac_Quad_DataHex_tBox.TabIndex = 10;
		this.ADCdac_Quad_Read_but.Location = new System.Drawing.Point(160, 68);
		this.ADCdac_Quad_Read_but.Name = "ADCdac_Quad_Read_but";
		this.ADCdac_Quad_Read_but.Size = new System.Drawing.Size(70, 23);
		this.ADCdac_Quad_Read_but.TabIndex = 9;
		this.ADCdac_Quad_Read_but.Text = "Read";
		this.ADCdac_Quad_Read_but.UseVisualStyleBackColor = true;
		this.ADCdac_Quad_Read_but.Click += new System.EventHandler(ReadAdc_Click);
		this.ADCdac_Quad_1Shot_but.Location = new System.Drawing.Point(80, 68);
		this.ADCdac_Quad_1Shot_but.Name = "ADCdac_Quad_1Shot_but";
		this.ADCdac_Quad_1Shot_but.Size = new System.Drawing.Size(70, 23);
		this.ADCdac_Quad_1Shot_but.TabIndex = 8;
		this.ADCdac_Quad_1Shot_but.Text = "1 Shot";
		this.ADCdac_Quad_1Shot_but.UseVisualStyleBackColor = true;
		this.ADCdac_Quad_1Shot_but.Click += new System.EventHandler(OneShotAdc_Click);
		this.ADCdac_Quad_Write_but.Location = new System.Drawing.Point(4, 68);
		this.ADCdac_Quad_Write_but.Name = "ADCdac_Quad_Write_but";
		this.ADCdac_Quad_Write_but.Size = new System.Drawing.Size(70, 23);
		this.ADCdac_Quad_Write_but.TabIndex = 7;
		this.ADCdac_Quad_Write_but.Text = "Write Conf";
		this.ADCdac_Quad_Write_but.UseVisualStyleBackColor = true;
		this.ADCdac_Quad_Write_but.Click += new System.EventHandler(WriteAdc_Click);
		this.Ext_QuadADC_ResGain_label.AutoSize = true;
		this.Ext_QuadADC_ResGain_label.Location = new System.Drawing.Point(147, 30);
		this.Ext_QuadADC_ResGain_label.Name = "Ext_QuadADC_ResGain_label";
		this.Ext_QuadADC_ResGain_label.Size = new System.Drawing.Size(91, 13);
		this.Ext_QuadADC_ResGain_label.TabIndex = 6;
		this.Ext_QuadADC_ResGain_label.Text = "Resol.   PGA gain";
		this.Ext_QuadADC_Val_Monitored_label.AutoSize = true;
		this.Ext_QuadADC_Val_Monitored_label.Location = new System.Drawing.Point(31, 30);
		this.Ext_QuadADC_Val_Monitored_label.Name = "Ext_QuadADC_Val_Monitored_label";
		this.Ext_QuadADC_Val_Monitored_label.Size = new System.Drawing.Size(71, 13);
		this.Ext_QuadADC_Val_Monitored_label.TabIndex = 5;
		this.Ext_QuadADC_Val_Monitored_label.Text = "Val monitored";
		this.ADCdac_Quad_Gain_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.ADCdac_Quad_Gain_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.ADCdac_Quad_Gain_comboBox.FormattingEnabled = true;
		this.ADCdac_Quad_Gain_comboBox.Items.AddRange(new object[4] { "x1", "x2", "x4", "x8" });
		this.ADCdac_Quad_Gain_comboBox.Location = new System.Drawing.Point(194, 43);
		this.ADCdac_Quad_Gain_comboBox.Name = "ADCdac_Quad_Gain_comboBox";
		this.ADCdac_Quad_Gain_comboBox.Size = new System.Drawing.Size(36, 21);
		this.ADCdac_Quad_Gain_comboBox.TabIndex = 4;
		this.ADCdac_Quad_Res_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.ADCdac_Quad_Res_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.ADCdac_Quad_Res_comboBox.FormattingEnabled = true;
		this.ADCdac_Quad_Res_comboBox.Items.AddRange(new object[3] { "12 bits", "14 bits", "16 bits" });
		this.ADCdac_Quad_Res_comboBox.Location = new System.Drawing.Point(140, 43);
		this.ADCdac_Quad_Res_comboBox.Name = "ADCdac_Quad_Res_comboBox";
		this.ADCdac_Quad_Res_comboBox.Size = new System.Drawing.Size(52, 21);
		this.ADCdac_Quad_Res_comboBox.TabIndex = 3;
		this.ADCdac_Quad_ch_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.ADCdac_Quad_ch_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.ADCdac_Quad_ch_comboBox.FormattingEnabled = true;
		this.ADCdac_Quad_ch_comboBox.Items.AddRange(new object[8] { "Vthr_H", "Vthr_L", "Vinj_H", "Vref_L", "Vfeed", "Vref", "V_Icap", "V_Iref" });
		this.ADCdac_Quad_ch_comboBox.Location = new System.Drawing.Point(27, 43);
		this.ADCdac_Quad_ch_comboBox.Name = "ADCdac_Quad_ch_comboBox";
		this.ADCdac_Quad_ch_comboBox.Size = new System.Drawing.Size(83, 21);
		this.ADCdac_Quad_ch_comboBox.TabIndex = 1;
		this.ADCdac_Quad_RDY_chkBox.AutoSize = true;
		this.ADCdac_Quad_RDY_chkBox.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.ADCdac_Quad_RDY_chkBox.Location = new System.Drawing.Point(1, 30);
		this.ADCdac_Quad_RDY_chkBox.Name = "ADCdac_Quad_RDY_chkBox";
		this.ADCdac_Quad_RDY_chkBox.Size = new System.Drawing.Size(34, 31);
		this.ADCdac_Quad_RDY_chkBox.TabIndex = 0;
		this.ADCdac_Quad_RDY_chkBox.Text = "RDY";
		this.ADCdac_Quad_RDY_chkBox.UseVisualStyleBackColor = true;
		this.ADCdac_Quad_OC_chkBox.AutoSize = true;
		this.ADCdac_Quad_OC_chkBox.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.ADCdac_Quad_OC_chkBox.Location = new System.Drawing.Point(107, 20);
		this.ADCdac_Quad_OC_chkBox.Name = "ADCdac_Quad_OC_chkBox";
		this.ADCdac_Quad_OC_chkBox.Size = new System.Drawing.Size(38, 44);
		this.ADCdac_Quad_OC_chkBox.TabIndex = 2;
		this.ADCdac_Quad_OC_chkBox.Text = "Conv\r\nMode";
		this.ADCdac_Quad_OC_chkBox.UseVisualStyleBackColor = true;
		this.Ext_QuadADC_HexDecV_label.AutoSize = true;
		this.Ext_QuadADC_HexDecV_label.Location = new System.Drawing.Point(36, 117);
		this.Ext_QuadADC_HexDecV_label.Name = "Ext_QuadADC_HexDecV_label";
		this.Ext_QuadADC_HexDecV_label.Size = new System.Drawing.Size(106, 13);
		this.Ext_QuadADC_HexDecV_label.TabIndex = 15;
		this.Ext_QuadADC_HexDecV_label.Text = "Hex       Dec        mV";
		this.CurMonADC_groupBox.BackColor = System.Drawing.Color.LightGreen;
		this.CurMonADC_groupBox.Controls.Add(this.ADCcommon_NShot_label);
		this.CurMonADC_groupBox.Controls.Add(this.ADCcommon_NShot_button);
		this.CurMonADC_groupBox.Controls.Add(this.ADCcommon_NShot_numUpDown);
		this.CurMonADC_groupBox.Controls.Add(this.ADCcommon_Value_tBox);
		this.CurMonADC_groupBox.Controls.Add(this.Ext_CommonADC_Config_label);
		this.CurMonADC_groupBox.Controls.Add(this.ADCcommon_Config_tBox);
		this.CurMonADC_groupBox.Controls.Add(this.ADCcommon_DataDec_tBox);
		this.CurMonADC_groupBox.Controls.Add(this.Ext_CommonADC_Data_label);
		this.CurMonADC_groupBox.Controls.Add(this.ADCcommon_DataHex_tBox);
		this.CurMonADC_groupBox.Controls.Add(this.ADCcommon_Read_but);
		this.CurMonADC_groupBox.Controls.Add(this.ADCcommon_1Shot_but);
		this.CurMonADC_groupBox.Controls.Add(this.ADCcommon_Write_but);
		this.CurMonADC_groupBox.Controls.Add(this.Ext_CommonADC_ResGain_label);
		this.CurMonADC_groupBox.Controls.Add(this.Ext_CommonADC_Val_Monitored_label);
		this.CurMonADC_groupBox.Controls.Add(this.ADCcommon_Gain_comboBox);
		this.CurMonADC_groupBox.Controls.Add(this.ADCcommon_Res_comboBox);
		this.CurMonADC_groupBox.Controls.Add(this.ADCcommon_ch_comboBox);
		this.CurMonADC_groupBox.Controls.Add(this.ADCcommon_RDY_chkBox);
		this.CurMonADC_groupBox.Controls.Add(this.ADCcommon_OC_chkBox);
		this.CurMonADC_groupBox.Controls.Add(this.Ext_CommonADC_HexDecV_label);
		this.CurMonADC_groupBox.Location = new System.Drawing.Point(5, 6);
		this.CurMonADC_groupBox.Name = "CurMonADC_groupBox";
		this.CurMonADC_groupBox.Size = new System.Drawing.Size(238, 157);
		this.CurMonADC_groupBox.TabIndex = 14;
		this.CurMonADC_groupBox.TabStop = false;
		this.CurMonADC_groupBox.Text = "Common Monitor ADC";
		this.ADCcommon_NShot_label.AutoSize = true;
		this.ADCcommon_NShot_label.Location = new System.Drawing.Point(6, 98);
		this.ADCcommon_NShot_label.Name = "ADCcommon_NShot_label";
		this.ADCcommon_NShot_label.Size = new System.Drawing.Size(15, 13);
		this.ADCcommon_NShot_label.TabIndex = 19;
		this.ADCcommon_NShot_label.Text = "N";
		this.ADCcommon_NShot_button.Location = new System.Drawing.Point(80, 94);
		this.ADCcommon_NShot_button.Name = "ADCcommon_NShot_button";
		this.ADCcommon_NShot_button.Size = new System.Drawing.Size(75, 23);
		this.ADCcommon_NShot_button.TabIndex = 18;
		this.ADCcommon_NShot_button.Text = "N Shot Avg";
		this.ADCcommon_NShot_button.UseVisualStyleBackColor = true;
		this.ADCcommon_NShot_button.Click += new System.EventHandler(NShotAdc_Click);
		this.ADCcommon_NShot_numUpDown.Location = new System.Drawing.Point(27, 95);
		this.ADCcommon_NShot_numUpDown.Maximum = new decimal(new int[4] { 3000, 0, 0, 0 });
		this.ADCcommon_NShot_numUpDown.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.ADCcommon_NShot_numUpDown.Name = "ADCcommon_NShot_numUpDown";
		this.ADCcommon_NShot_numUpDown.Size = new System.Drawing.Size(44, 20);
		this.ADCcommon_NShot_numUpDown.TabIndex = 17;
		this.ADCcommon_NShot_numUpDown.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.ADCcommon_Value_tBox.Location = new System.Drawing.Point(107, 130);
		this.ADCcommon_Value_tBox.Name = "ADCcommon_Value_tBox";
		this.ADCcommon_Value_tBox.ReadOnly = true;
		this.ADCcommon_Value_tBox.Size = new System.Drawing.Size(53, 20);
		this.ADCcommon_Value_tBox.TabIndex = 16;
		this.ADCcommon_Value_tBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.Ext_CommonADC_Config_label.AutoSize = true;
		this.Ext_CommonADC_Config_label.Location = new System.Drawing.Point(166, 124);
		this.Ext_CommonADC_Config_label.Name = "Ext_CommonADC_Config_label";
		this.Ext_CommonADC_Config_label.Size = new System.Drawing.Size(36, 26);
		this.Ext_CommonADC_Config_label.TabIndex = 14;
		this.Ext_CommonADC_Config_label.Text = "ADC\r\nconfig";
		this.ADCcommon_Config_tBox.Location = new System.Drawing.Point(203, 129);
		this.ADCcommon_Config_tBox.Name = "ADCcommon_Config_tBox";
		this.ADCcommon_Config_tBox.Size = new System.Drawing.Size(20, 20);
		this.ADCcommon_Config_tBox.TabIndex = 13;
		this.ADCcommon_Config_tBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
		this.ADCcommon_DataDec_tBox.Location = new System.Drawing.Point(68, 130);
		this.ADCcommon_DataDec_tBox.Name = "ADCcommon_DataDec_tBox";
		this.ADCcommon_DataDec_tBox.ReadOnly = true;
		this.ADCcommon_DataDec_tBox.Size = new System.Drawing.Size(39, 20);
		this.ADCcommon_DataDec_tBox.TabIndex = 12;
		this.ADCcommon_DataDec_tBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.Ext_CommonADC_Data_label.AutoSize = true;
		this.Ext_CommonADC_Data_label.Location = new System.Drawing.Point(3, 124);
		this.Ext_CommonADC_Data_label.Name = "Ext_CommonADC_Data_label";
		this.Ext_CommonADC_Data_label.Size = new System.Drawing.Size(32, 26);
		this.Ext_CommonADC_Data_label.TabIndex = 11;
		this.Ext_CommonADC_Data_label.Text = "ADC \r\ndata";
		this.ADCcommon_DataHex_tBox.Location = new System.Drawing.Point(34, 130);
		this.ADCcommon_DataHex_tBox.Name = "ADCcommon_DataHex_tBox";
		this.ADCcommon_DataHex_tBox.ReadOnly = true;
		this.ADCcommon_DataHex_tBox.Size = new System.Drawing.Size(32, 20);
		this.ADCcommon_DataHex_tBox.TabIndex = 10;
		this.ADCcommon_Read_but.Location = new System.Drawing.Point(160, 68);
		this.ADCcommon_Read_but.Name = "ADCcommon_Read_but";
		this.ADCcommon_Read_but.Size = new System.Drawing.Size(70, 23);
		this.ADCcommon_Read_but.TabIndex = 9;
		this.ADCcommon_Read_but.Text = "Read";
		this.ADCcommon_Read_but.UseVisualStyleBackColor = true;
		this.ADCcommon_Read_but.Click += new System.EventHandler(ReadAdc_Click);
		this.ADCcommon_1Shot_but.Location = new System.Drawing.Point(80, 68);
		this.ADCcommon_1Shot_but.Name = "ADCcommon_1Shot_but";
		this.ADCcommon_1Shot_but.Size = new System.Drawing.Size(70, 23);
		this.ADCcommon_1Shot_but.TabIndex = 8;
		this.ADCcommon_1Shot_but.Text = "1 Shot";
		this.ADCcommon_1Shot_but.UseVisualStyleBackColor = true;
		this.ADCcommon_1Shot_but.Click += new System.EventHandler(OneShotAdc_Click);
		this.ADCcommon_Write_but.Location = new System.Drawing.Point(4, 68);
		this.ADCcommon_Write_but.Name = "ADCcommon_Write_but";
		this.ADCcommon_Write_but.Size = new System.Drawing.Size(70, 23);
		this.ADCcommon_Write_but.TabIndex = 7;
		this.ADCcommon_Write_but.Text = "Write Conf";
		this.ADCcommon_Write_but.UseVisualStyleBackColor = true;
		this.ADCcommon_Write_but.Click += new System.EventHandler(WriteAdc_Click);
		this.Ext_CommonADC_ResGain_label.AutoSize = true;
		this.Ext_CommonADC_ResGain_label.Location = new System.Drawing.Point(147, 30);
		this.Ext_CommonADC_ResGain_label.Name = "Ext_CommonADC_ResGain_label";
		this.Ext_CommonADC_ResGain_label.Size = new System.Drawing.Size(91, 13);
		this.Ext_CommonADC_ResGain_label.TabIndex = 6;
		this.Ext_CommonADC_ResGain_label.Text = "Resol.   PGA gain";
		this.Ext_CommonADC_Val_Monitored_label.AutoSize = true;
		this.Ext_CommonADC_Val_Monitored_label.Location = new System.Drawing.Point(31, 30);
		this.Ext_CommonADC_Val_Monitored_label.Name = "Ext_CommonADC_Val_Monitored_label";
		this.Ext_CommonADC_Val_Monitored_label.Size = new System.Drawing.Size(71, 13);
		this.Ext_CommonADC_Val_Monitored_label.TabIndex = 5;
		this.Ext_CommonADC_Val_Monitored_label.Text = "Val monitored";
		this.ADCcommon_Gain_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.ADCcommon_Gain_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.ADCcommon_Gain_comboBox.FormattingEnabled = true;
		this.ADCcommon_Gain_comboBox.Items.AddRange(new object[4] { "x1", "x2", "x4", "x8" });
		this.ADCcommon_Gain_comboBox.Location = new System.Drawing.Point(194, 43);
		this.ADCcommon_Gain_comboBox.Name = "ADCcommon_Gain_comboBox";
		this.ADCcommon_Gain_comboBox.Size = new System.Drawing.Size(36, 21);
		this.ADCcommon_Gain_comboBox.TabIndex = 4;
		this.ADCcommon_Res_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.ADCcommon_Res_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.ADCcommon_Res_comboBox.FormattingEnabled = true;
		this.ADCcommon_Res_comboBox.Items.AddRange(new object[3] { "12 bits", "14 bits", "16 bits" });
		this.ADCcommon_Res_comboBox.Location = new System.Drawing.Point(140, 43);
		this.ADCcommon_Res_comboBox.Name = "ADCcommon_Res_comboBox";
		this.ADCcommon_Res_comboBox.Size = new System.Drawing.Size(52, 21);
		this.ADCcommon_Res_comboBox.TabIndex = 3;
		this.ADCcommon_ch_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.ADCcommon_ch_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.ADCcommon_ch_comboBox.FormattingEnabled = true;
		this.ADCcommon_ch_comboBox.Items.AddRange(new object[8] { "I_D_0.9 V", "I_D_1.2 V", "I_A_0.9 V", "I_A_1.2 V", "Vext_DAC", "NONE", "NONE ", "Vref_DAC" });
		this.ADCcommon_ch_comboBox.Location = new System.Drawing.Point(27, 43);
		this.ADCcommon_ch_comboBox.Name = "ADCcommon_ch_comboBox";
		this.ADCcommon_ch_comboBox.Size = new System.Drawing.Size(83, 21);
		this.ADCcommon_ch_comboBox.TabIndex = 1;
		this.ADCcommon_RDY_chkBox.AutoSize = true;
		this.ADCcommon_RDY_chkBox.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.ADCcommon_RDY_chkBox.Location = new System.Drawing.Point(1, 30);
		this.ADCcommon_RDY_chkBox.Name = "ADCcommon_RDY_chkBox";
		this.ADCcommon_RDY_chkBox.Size = new System.Drawing.Size(34, 31);
		this.ADCcommon_RDY_chkBox.TabIndex = 0;
		this.ADCcommon_RDY_chkBox.Text = "RDY";
		this.ADCcommon_RDY_chkBox.UseVisualStyleBackColor = true;
		this.ADCcommon_OC_chkBox.AutoSize = true;
		this.ADCcommon_OC_chkBox.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.ADCcommon_OC_chkBox.Location = new System.Drawing.Point(107, 20);
		this.ADCcommon_OC_chkBox.Name = "ADCcommon_OC_chkBox";
		this.ADCcommon_OC_chkBox.Size = new System.Drawing.Size(38, 44);
		this.ADCcommon_OC_chkBox.TabIndex = 2;
		this.ADCcommon_OC_chkBox.Text = "Conv\r\nMode";
		this.ADCcommon_OC_chkBox.UseVisualStyleBackColor = true;
		this.Ext_CommonADC_HexDecV_label.AutoSize = true;
		this.Ext_CommonADC_HexDecV_label.Location = new System.Drawing.Point(36, 118);
		this.Ext_CommonADC_HexDecV_label.Name = "Ext_CommonADC_HexDecV_label";
		this.Ext_CommonADC_HexDecV_label.Size = new System.Drawing.Size(109, 13);
		this.Ext_CommonADC_HexDecV_label.TabIndex = 15;
		this.Ext_CommonADC_HexDecV_label.Text = "Hex       Dec         mV";
		this.ExtDAC_groupBox.BackColor = System.Drawing.Color.LightGreen;
		this.ExtDAC_groupBox.Controls.Add(this.Ext_Common_Vref_label);
		this.ExtDAC_groupBox.Controls.Add(this.DACvref_L_UpDown);
		this.ExtDAC_groupBox.Controls.Add(this.DACvref_L_but);
		this.ExtDAC_groupBox.Controls.Add(this.DACVext_but);
		this.ExtDAC_groupBox.Controls.Add(this.VDDA_txtBox);
		this.ExtDAC_groupBox.Controls.Add(this.Ext_Common_VDDA_label);
		this.ExtDAC_groupBox.Controls.Add(this.DACvthr_H_UpDown);
		this.ExtDAC_groupBox.Controls.Add(this.DACvinj_H_but);
		this.ExtDAC_groupBox.Controls.Add(this.DACvthr_L_UpDown);
		this.ExtDAC_groupBox.Controls.Add(this.DACvthr_L_but);
		this.ExtDAC_groupBox.Controls.Add(this.DACvinj_H_UpDown);
		this.ExtDAC_groupBox.Controls.Add(this.DACvthr_H_but);
		this.ExtDAC_groupBox.Controls.Add(this.DACVext_UpDown);
		this.ExtDAC_groupBox.Controls.Add(this.Ext_Common_VthVinjVext_label);
		this.ExtDAC_groupBox.Location = new System.Drawing.Point(6, 167);
		this.ExtDAC_groupBox.Name = "ExtDAC_groupBox";
		this.ExtDAC_groupBox.Size = new System.Drawing.Size(234, 195);
		this.ExtDAC_groupBox.TabIndex = 13;
		this.ExtDAC_groupBox.TabStop = false;
		this.ExtDAC_groupBox.Text = "External Common DACs";
		this.Ext_Common_Vref_label.AutoSize = true;
		this.Ext_Common_Vref_label.Location = new System.Drawing.Point(85, 60);
		this.Ext_Common_Vref_label.Name = "Ext_Common_Vref_label";
		this.Ext_Common_Vref_label.Size = new System.Drawing.Size(50, 13);
		this.Ext_Common_Vref_label.TabIndex = 15;
		this.Ext_Common_Vref_label.Text = "Vref (mV)";
		this.DACvref_L_UpDown.DecimalPlaces = 2;
		this.DACvref_L_UpDown.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.DACvref_L_UpDown.Location = new System.Drawing.Point(13, 56);
		this.DACvref_L_UpDown.Maximum = new decimal(new int[4] { 1200, 0, 0, 0 });
		this.DACvref_L_UpDown.Name = "DACvref_L_UpDown";
		this.DACvref_L_UpDown.Size = new System.Drawing.Size(71, 20);
		this.DACvref_L_UpDown.TabIndex = 13;
		this.DACvref_L_UpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.DACvref_L_UpDown.Value = new decimal(new int[4] { 900, 0, 0, 0 });
		this.DACvref_L_but.Location = new System.Drawing.Point(149, 55);
		this.DACvref_L_but.Name = "DACvref_L_but";
		this.DACvref_L_but.Size = new System.Drawing.Size(74, 23);
		this.DACvref_L_but.TabIndex = 14;
		this.DACvref_L_but.Text = "Write Vref_L";
		this.DACvref_L_but.UseVisualStyleBackColor = true;
		this.DACvref_L_but.Click += new System.EventHandler(WriteDac_Click);
		this.DACVext_but.Location = new System.Drawing.Point(149, 162);
		this.DACVext_but.Name = "DACVext_but";
		this.DACVext_but.Size = new System.Drawing.Size(74, 23);
		this.DACVext_but.TabIndex = 10;
		this.DACVext_but.Text = "Write Vext";
		this.DACVext_but.UseVisualStyleBackColor = true;
		this.DACVext_but.Click += new System.EventHandler(WriteDac_Click);
		this.VDDA_txtBox.Location = new System.Drawing.Point(13, 28);
		this.VDDA_txtBox.Name = "VDDA_txtBox";
		this.VDDA_txtBox.Size = new System.Drawing.Size(48, 20);
		this.VDDA_txtBox.TabIndex = 0;
		this.VDDA_txtBox.Text = "900";
		this.VDDA_txtBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.Ext_Common_VDDA_label.AutoSize = true;
		this.Ext_Common_VDDA_label.Location = new System.Drawing.Point(67, 35);
		this.Ext_Common_VDDA_label.Name = "Ext_Common_VDDA_label";
		this.Ext_Common_VDDA_label.Size = new System.Drawing.Size(61, 13);
		this.Ext_Common_VDDA_label.TabIndex = 1;
		this.Ext_Common_VDDA_label.Text = "VDDA (mV)";
		this.DACvthr_H_UpDown.DecimalPlaces = 2;
		this.DACvthr_H_UpDown.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.DACvthr_H_UpDown.Location = new System.Drawing.Point(13, 85);
		this.DACvthr_H_UpDown.Maximum = new decimal(new int[4] { 1200, 0, 0, 0 });
		this.DACvthr_H_UpDown.Name = "DACvthr_H_UpDown";
		this.DACvthr_H_UpDown.Size = new System.Drawing.Size(71, 20);
		this.DACvthr_H_UpDown.TabIndex = 2;
		this.DACvthr_H_UpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.DACvthr_H_UpDown.Value = new decimal(new int[4] { 900, 0, 0, 0 });
		this.DACvinj_H_but.Location = new System.Drawing.Point(149, 136);
		this.DACvinj_H_but.Name = "DACvinj_H_but";
		this.DACvinj_H_but.Size = new System.Drawing.Size(74, 23);
		this.DACvinj_H_but.TabIndex = 9;
		this.DACvinj_H_but.Text = "Write Vinj_H";
		this.DACvinj_H_but.UseVisualStyleBackColor = true;
		this.DACvinj_H_but.Click += new System.EventHandler(WriteDac_Click);
		this.DACvthr_L_UpDown.DecimalPlaces = 2;
		this.DACvthr_L_UpDown.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.DACvthr_L_UpDown.Location = new System.Drawing.Point(13, 111);
		this.DACvthr_L_UpDown.Maximum = new decimal(new int[4] { 1000, 0, 0, 0 });
		this.DACvthr_L_UpDown.Name = "DACvthr_L_UpDown";
		this.DACvthr_L_UpDown.Size = new System.Drawing.Size(71, 20);
		this.DACvthr_L_UpDown.TabIndex = 3;
		this.DACvthr_L_UpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.DACvthr_L_UpDown.Value = new decimal(new int[4] { 380, 0, 0, 0 });
		this.DACvthr_L_but.Location = new System.Drawing.Point(149, 110);
		this.DACvthr_L_but.Name = "DACvthr_L_but";
		this.DACvthr_L_but.Size = new System.Drawing.Size(74, 23);
		this.DACvthr_L_but.TabIndex = 8;
		this.DACvthr_L_but.Text = "Write Vth_L";
		this.DACvthr_L_but.UseVisualStyleBackColor = true;
		this.DACvthr_L_but.Click += new System.EventHandler(WriteDac_Click);
		this.DACvinj_H_UpDown.DecimalPlaces = 2;
		this.DACvinj_H_UpDown.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.DACvinj_H_UpDown.Location = new System.Drawing.Point(13, 137);
		this.DACvinj_H_UpDown.Maximum = new decimal(new int[4] { 1000, 0, 0, 0 });
		this.DACvinj_H_UpDown.Name = "DACvinj_H_UpDown";
		this.DACvinj_H_UpDown.Size = new System.Drawing.Size(71, 20);
		this.DACvinj_H_UpDown.TabIndex = 4;
		this.DACvinj_H_UpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.DACvinj_H_UpDown.Value = new decimal(new int[4] { 600, 0, 0, 0 });
		this.DACvthr_H_but.Location = new System.Drawing.Point(149, 84);
		this.DACvthr_H_but.Name = "DACvthr_H_but";
		this.DACvthr_H_but.Size = new System.Drawing.Size(74, 23);
		this.DACvthr_H_but.TabIndex = 7;
		this.DACvthr_H_but.Text = "Write Vth_H";
		this.DACvthr_H_but.UseVisualStyleBackColor = true;
		this.DACvthr_H_but.Click += new System.EventHandler(WriteDac_Click);
		this.DACVext_UpDown.DecimalPlaces = 2;
		this.DACVext_UpDown.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.DACVext_UpDown.Location = new System.Drawing.Point(13, 163);
		this.DACVext_UpDown.Maximum = new decimal(new int[4] { 1000, 0, 0, 0 });
		this.DACVext_UpDown.Name = "DACVext_UpDown";
		this.DACVext_UpDown.Size = new System.Drawing.Size(71, 20);
		this.DACVext_UpDown.TabIndex = 5;
		this.DACVext_UpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
		this.DACVext_UpDown.Value = new decimal(new int[4] { 400, 0, 0, 0 });
		this.Ext_Common_VthVinjVext_label.AutoSize = true;
		this.Ext_Common_VthVinjVext_label.Location = new System.Drawing.Point(85, 89);
		this.Ext_Common_VthVinjVext_label.Name = "Ext_Common_VthVinjVext_label";
		this.Ext_Common_VthVinjVext_label.Size = new System.Drawing.Size(62, 91);
		this.Ext_Common_VthVinjVext_label.TabIndex = 6;
		this.Ext_Common_VthVinjVext_label.Text = "Vth_H (mV)\r\n\r\nVth_L (mV)\r\n\r\nVinj_H (mV)\r\n\r\nVext ( mV)";
		this.Ctrl_tabPage.BackColor = System.Drawing.Color.LightGray;
		this.Ctrl_tabPage.Controls.Add(this.groupBox3);
		this.Ctrl_tabPage.Location = new System.Drawing.Point(4, 25);
		this.Ctrl_tabPage.Name = "Ctrl_tabPage";
		this.Ctrl_tabPage.Padding = new System.Windows.Forms.Padding(3);
		this.Ctrl_tabPage.Size = new System.Drawing.Size(942, 410);
		this.Ctrl_tabPage.TabIndex = 4;
		this.Ctrl_tabPage.Text = "Commands";
		this.groupBox3.BackColor = System.Drawing.Color.DeepSkyBlue;
		this.groupBox3.Controls.Add(this.AllMAT_AllAFE_OFF_but);
		this.groupBox3.Controls.Add(this.AllMAT_AllAFE_ON_but);
		this.groupBox3.Controls.Add(this.AllMAT_AllTDC_OFF_but);
		this.groupBox3.Controls.Add(this.AllMAT_AllTDC_ON_but);
		this.groupBox3.Controls.Add(this.AllMAT_AllPIX_OFF_but);
		this.groupBox3.Controls.Add(this.AllMAT_AllPIX_ON_but);
		this.groupBox3.Location = new System.Drawing.Point(238, 6);
		this.groupBox3.Name = "groupBox3";
		this.groupBox3.Size = new System.Drawing.Size(225, 135);
		this.groupBox3.TabIndex = 0;
		this.groupBox3.TabStop = false;
		this.groupBox3.Text = "All MAT Commands";
		this.AllMAT_AllAFE_OFF_but.Location = new System.Drawing.Point(120, 95);
		this.AllMAT_AllAFE_OFF_but.Name = "AllMAT_AllAFE_OFF_but";
		this.AllMAT_AllAFE_OFF_but.Size = new System.Drawing.Size(85, 25);
		this.AllMAT_AllAFE_OFF_but.TabIndex = 5;
		this.AllMAT_AllAFE_OFF_but.Text = "ALL AFE OFF";
		this.AllMAT_AllAFE_OFF_but.UseVisualStyleBackColor = true;
		this.AllMAT_AllAFE_OFF_but.Click += new System.EventHandler(Global_ONorOFF_but_Click);
		this.AllMAT_AllAFE_ON_but.Location = new System.Drawing.Point(20, 95);
		this.AllMAT_AllAFE_ON_but.Name = "AllMAT_AllAFE_ON_but";
		this.AllMAT_AllAFE_ON_but.Size = new System.Drawing.Size(85, 25);
		this.AllMAT_AllAFE_ON_but.TabIndex = 4;
		this.AllMAT_AllAFE_ON_but.Text = "ALL AFE ON";
		this.AllMAT_AllAFE_ON_but.UseVisualStyleBackColor = true;
		this.AllMAT_AllAFE_ON_but.Click += new System.EventHandler(Global_ONorOFF_but_Click);
		this.AllMAT_AllTDC_OFF_but.Location = new System.Drawing.Point(120, 60);
		this.AllMAT_AllTDC_OFF_but.Name = "AllMAT_AllTDC_OFF_but";
		this.AllMAT_AllTDC_OFF_but.Size = new System.Drawing.Size(85, 25);
		this.AllMAT_AllTDC_OFF_but.TabIndex = 3;
		this.AllMAT_AllTDC_OFF_but.Text = "ALL TDC OFF";
		this.AllMAT_AllTDC_OFF_but.UseVisualStyleBackColor = true;
		this.AllMAT_AllTDC_OFF_but.Click += new System.EventHandler(Global_ONorOFF_but_Click);
		this.AllMAT_AllTDC_ON_but.Location = new System.Drawing.Point(20, 60);
		this.AllMAT_AllTDC_ON_but.Name = "AllMAT_AllTDC_ON_but";
		this.AllMAT_AllTDC_ON_but.Size = new System.Drawing.Size(85, 25);
		this.AllMAT_AllTDC_ON_but.TabIndex = 2;
		this.AllMAT_AllTDC_ON_but.Text = "ALL TDC ON";
		this.AllMAT_AllTDC_ON_but.UseVisualStyleBackColor = true;
		this.AllMAT_AllTDC_ON_but.Click += new System.EventHandler(Global_ONorOFF_but_Click);
		this.AllMAT_AllPIX_OFF_but.Location = new System.Drawing.Point(120, 25);
		this.AllMAT_AllPIX_OFF_but.Name = "AllMAT_AllPIX_OFF_but";
		this.AllMAT_AllPIX_OFF_but.Size = new System.Drawing.Size(85, 25);
		this.AllMAT_AllPIX_OFF_but.TabIndex = 1;
		this.AllMAT_AllPIX_OFF_but.Text = "ALL PIX OFF";
		this.AllMAT_AllPIX_OFF_but.UseVisualStyleBackColor = true;
		this.AllMAT_AllPIX_OFF_but.Click += new System.EventHandler(Global_ONorOFF_but_Click);
		this.AllMAT_AllPIX_ON_but.Location = new System.Drawing.Point(20, 25);
		this.AllMAT_AllPIX_ON_but.Name = "AllMAT_AllPIX_ON_but";
		this.AllMAT_AllPIX_ON_but.Size = new System.Drawing.Size(85, 25);
		this.AllMAT_AllPIX_ON_but.TabIndex = 0;
		this.AllMAT_AllPIX_ON_but.Text = "ALL PIX ON";
		this.AllMAT_AllPIX_ON_but.UseVisualStyleBackColor = true;
		this.AllMAT_AllPIX_ON_but.Click += new System.EventHandler(Global_ONorOFF_but_Click);
		this.Mat_tabPage.BackColor = System.Drawing.Color.LightGray;
		this.Mat_tabPage.Controls.Add(this.MAT_Test_Routines_groupBox);
		this.Mat_tabPage.Controls.Add(this.Visualize_DCOCalibration_but);
		this.Mat_tabPage.Controls.Add(this.MatSelQuad_SE_chkBox);
		this.Mat_tabPage.Controls.Add(this.MatSelQuad_NE_chkBox);
		this.Mat_tabPage.Controls.Add(this.MatSelQuad_SW_chkBox);
		this.Mat_tabPage.Controls.Add(this.MatSelQuad_NW_chkBox);
		this.Mat_tabPage.Controls.Add(this.MAT_AFE_groupBox);
		this.Mat_tabPage.Controls.Add(this.PIX_groupBox);
		this.Mat_tabPage.Controls.Add(this.MAT_DAC_groupBox);
		this.Mat_tabPage.Controls.Add(this.MAT_cfg_groupBox);
		this.Mat_tabPage.Controls.Add(this.MAT_COMMANDS_groupBox);
		this.Mat_tabPage.Controls.Add(this.MatI2C_read_all_but);
		this.Mat_tabPage.Controls.Add(this.MatI2C_write_all_but);
		this.Mat_tabPage.Controls.Add(this.MatI2C_read_single_but);
		this.Mat_tabPage.Controls.Add(this.MatI2C_write_single_but);
		this.Mat_tabPage.Controls.Add(this.Mat_dGridView);
		this.Mat_tabPage.Controls.Add(this.MatAddr_comboBox);
		this.Mat_tabPage.Controls.Add(this.MAT_I2C_addr_tBox);
		this.Mat_tabPage.Location = new System.Drawing.Point(4, 25);
		this.Mat_tabPage.Name = "Mat_tabPage";
		this.Mat_tabPage.Padding = new System.Windows.Forms.Padding(3);
		this.Mat_tabPage.Size = new System.Drawing.Size(942, 410);
		this.Mat_tabPage.TabIndex = 2;
		this.Mat_tabPage.Text = "MATTONELLA";
		this.MAT_Test_Routines_groupBox.BackColor = System.Drawing.Color.Tomato;
		this.MAT_Test_Routines_groupBox.Controls.Add(this.TestATP_but);
		this.MAT_Test_Routines_groupBox.Controls.Add(this.CalDCO_save_but);
		this.MAT_Test_Routines_groupBox.Controls.Add(this.MAT_DAC_VLDO_tmp_but);
		this.MAT_Test_Routines_groupBox.Controls.Add(this.CalibDCO_but);
		this.MAT_Test_Routines_groupBox.Controls.Add(this.TestDCO_but);
		this.MAT_Test_Routines_groupBox.Controls.Add(this.TestTDC_but);
		this.MAT_Test_Routines_groupBox.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.MAT_Test_Routines_groupBox.Location = new System.Drawing.Point(124, 289);
		this.MAT_Test_Routines_groupBox.Name = "MAT_Test_Routines_groupBox";
		this.MAT_Test_Routines_groupBox.Size = new System.Drawing.Size(414, 89);
		this.MAT_Test_Routines_groupBox.TabIndex = 54;
		this.MAT_Test_Routines_groupBox.TabStop = false;
		this.MAT_Test_Routines_groupBox.Text = "Automatic Routines";
		this.TestATP_but.BackColor = System.Drawing.Color.Gold;
		this.TestATP_but.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Yellow;
		this.TestATP_but.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.TestATP_but.Location = new System.Drawing.Point(251, 14);
		this.TestATP_but.Name = "TestATP_but";
		this.TestATP_but.Size = new System.Drawing.Size(75, 23);
		this.TestATP_but.TabIndex = 53;
		this.TestATP_but.Text = "Test ATP";
		this.TestATP_but.UseVisualStyleBackColor = false;
		this.TestATP_but.Click += new System.EventHandler(ATPtest_but_click);
		this.CalDCO_save_but.BackColor = System.Drawing.Color.SkyBlue;
		this.CalDCO_save_but.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Aqua;
		this.CalDCO_save_but.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.CalDCO_save_but.Location = new System.Drawing.Point(8, 40);
		this.CalDCO_save_but.Name = "CalDCO_save_but";
		this.CalDCO_save_but.Size = new System.Drawing.Size(75, 40);
		this.CalDCO_save_but.TabIndex = 52;
		this.CalDCO_save_but.Text = "Save DCO Calibration";
		this.CalDCO_save_but.UseVisualStyleBackColor = false;
		this.CalDCO_save_but.Click += new System.EventHandler(DCOcalib_save_but_click);
		this.MAT_DAC_VLDO_tmp_but.BackColor = System.Drawing.Color.Gold;
		this.MAT_DAC_VLDO_tmp_but.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.MAT_DAC_VLDO_tmp_but.Location = new System.Drawing.Point(332, 14);
		this.MAT_DAC_VLDO_tmp_but.Name = "MAT_DAC_VLDO_tmp_but";
		this.MAT_DAC_VLDO_tmp_but.Size = new System.Drawing.Size(75, 23);
		this.MAT_DAC_VLDO_tmp_but.TabIndex = 51;
		this.MAT_DAC_VLDO_tmp_but.Text = "Test DAC";
		this.MAT_DAC_VLDO_tmp_but.UseVisualStyleBackColor = false;
		this.MAT_DAC_VLDO_tmp_but.Click += new System.EventHandler(MAT_DAC_VLDO_tmp_butClick);
		this.CalibDCO_but.BackColor = System.Drawing.Color.Gold;
		this.CalibDCO_but.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Yellow;
		this.CalibDCO_but.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.CalibDCO_but.Location = new System.Drawing.Point(8, 14);
		this.CalibDCO_but.Name = "CalibDCO_but";
		this.CalibDCO_but.Size = new System.Drawing.Size(75, 23);
		this.CalibDCO_but.TabIndex = 23;
		this.CalibDCO_but.Text = "Calib DCO";
		this.CalibDCO_but.UseVisualStyleBackColor = false;
		this.CalibDCO_but.Click += new System.EventHandler(DCOcalibration_but_click);
		this.TestDCO_but.BackColor = System.Drawing.Color.Gold;
		this.TestDCO_but.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Yellow;
		this.TestDCO_but.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.TestDCO_but.Location = new System.Drawing.Point(89, 14);
		this.TestDCO_but.Name = "TestDCO_but";
		this.TestDCO_but.Size = new System.Drawing.Size(75, 23);
		this.TestDCO_but.TabIndex = 22;
		this.TestDCO_but.Text = "Test DCO";
		this.TestDCO_but.UseVisualStyleBackColor = false;
		this.TestDCO_but.Click += new System.EventHandler(DCOtest_but_click);
		this.TestTDC_but.BackColor = System.Drawing.Color.Gold;
		this.TestTDC_but.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Yellow;
		this.TestTDC_but.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.TestTDC_but.Location = new System.Drawing.Point(170, 14);
		this.TestTDC_but.Name = "TestTDC_but";
		this.TestTDC_but.Size = new System.Drawing.Size(75, 23);
		this.TestTDC_but.TabIndex = 21;
		this.TestTDC_but.Text = "Test TDC";
		this.TestTDC_but.UseVisualStyleBackColor = false;
		this.TestTDC_but.Click += new System.EventHandler(TDCtest_but_click);
		this.Visualize_DCOCalibration_but.BackColor = System.Drawing.Color.SkyBlue;
		this.Visualize_DCOCalibration_but.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Aqua;
		this.Visualize_DCOCalibration_but.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.Visualize_DCOCalibration_but.Location = new System.Drawing.Point(445, 381);
		this.Visualize_DCOCalibration_but.Name = "Visualize_DCOCalibration_but";
		this.Visualize_DCOCalibration_but.Size = new System.Drawing.Size(90, 25);
		this.Visualize_DCOCalibration_but.TabIndex = 53;
		this.Visualize_DCOCalibration_but.Text = "See DCO CAL";
		this.Visualize_DCOCalibration_but.UseVisualStyleBackColor = false;
		this.Visualize_DCOCalibration_but.Click += new System.EventHandler(CALForm_but_click);
		this.MatSelQuad_SE_chkBox.AutoSize = true;
		this.MatSelQuad_SE_chkBox.Location = new System.Drawing.Point(50, 390);
		this.MatSelQuad_SE_chkBox.Name = "MatSelQuad_SE_chkBox";
		this.MatSelQuad_SE_chkBox.Size = new System.Drawing.Size(40, 17);
		this.MatSelQuad_SE_chkBox.TabIndex = 50;
		this.MatSelQuad_SE_chkBox.Text = "SE";
		this.MatSelQuad_SE_chkBox.UseVisualStyleBackColor = true;
		this.MatSelQuad_SE_chkBox.CheckedChanged += new System.EventHandler(Mat_Quad_Sel_Change);
		this.MatSelQuad_NE_chkBox.AutoSize = true;
		this.MatSelQuad_NE_chkBox.Location = new System.Drawing.Point(50, 368);
		this.MatSelQuad_NE_chkBox.Name = "MatSelQuad_NE_chkBox";
		this.MatSelQuad_NE_chkBox.Size = new System.Drawing.Size(41, 17);
		this.MatSelQuad_NE_chkBox.TabIndex = 49;
		this.MatSelQuad_NE_chkBox.Text = "NE";
		this.MatSelQuad_NE_chkBox.UseVisualStyleBackColor = true;
		this.MatSelQuad_NE_chkBox.CheckedChanged += new System.EventHandler(Mat_Quad_Sel_Change);
		this.MatSelQuad_SW_chkBox.AutoSize = true;
		this.MatSelQuad_SW_chkBox.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.MatSelQuad_SW_chkBox.Location = new System.Drawing.Point(3, 390);
		this.MatSelQuad_SW_chkBox.Name = "MatSelQuad_SW_chkBox";
		this.MatSelQuad_SW_chkBox.Size = new System.Drawing.Size(44, 17);
		this.MatSelQuad_SW_chkBox.TabIndex = 48;
		this.MatSelQuad_SW_chkBox.Text = "SW";
		this.MatSelQuad_SW_chkBox.UseVisualStyleBackColor = true;
		this.MatSelQuad_SW_chkBox.CheckedChanged += new System.EventHandler(Mat_Quad_Sel_Change);
		this.MatSelQuad_NW_chkBox.AutoSize = true;
		this.MatSelQuad_NW_chkBox.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.MatSelQuad_NW_chkBox.Location = new System.Drawing.Point(3, 368);
		this.MatSelQuad_NW_chkBox.Name = "MatSelQuad_NW_chkBox";
		this.MatSelQuad_NW_chkBox.Size = new System.Drawing.Size(45, 17);
		this.MatSelQuad_NW_chkBox.TabIndex = 47;
		this.MatSelQuad_NW_chkBox.Text = "NW";
		this.MatSelQuad_NW_chkBox.UseVisualStyleBackColor = true;
		this.MatSelQuad_NW_chkBox.CheckedChanged += new System.EventHandler(Mat_Quad_Sel_Change);
		this.MAT_AFE_groupBox.BackColor = System.Drawing.Color.Violet;
		this.MAT_AFE_groupBox.Controls.Add(this.label1);
		this.MAT_AFE_groupBox.Controls.Add(this.AFE_MAT_Sel_comboBox);
		this.MAT_AFE_groupBox.Controls.Add(this.SEL_VINJ_MUX_Low_label);
		this.MAT_AFE_groupBox.Controls.Add(this.SEL_VINJ_MUX_Low_comboBox);
		this.MAT_AFE_groupBox.Controls.Add(this.EN_P_VTH_chkBox);
		this.MAT_AFE_groupBox.Controls.Add(this.EXT_DC_chkBox);
		this.MAT_AFE_groupBox.Controls.Add(this.AFE_LB_chkBox);
		this.MAT_AFE_groupBox.Controls.Add(this.SEL_VINJ_MUX_High_comboBox);
		this.MAT_AFE_groupBox.Controls.Add(this.AFE_AUTO_chkBox);
		this.MAT_AFE_groupBox.Controls.Add(this.EN_P_VLDO_chkBox);
		this.MAT_AFE_groupBox.Controls.Add(this.CON_PAD_chkBox);
		this.MAT_AFE_groupBox.Controls.Add(this.EN_P_VFB_chkBox);
		this.MAT_AFE_groupBox.Controls.Add(this.EN_P_VINJ_chkBox);
		this.MAT_AFE_groupBox.Controls.Add(this.SEL_VINJ_MUX_High_label);
		this.MAT_AFE_groupBox.Location = new System.Drawing.Point(754, 5);
		this.MAT_AFE_groupBox.Name = "MAT_AFE_groupBox";
		this.MAT_AFE_groupBox.Size = new System.Drawing.Size(185, 144);
		this.MAT_AFE_groupBox.TabIndex = 46;
		this.MAT_AFE_groupBox.TabStop = false;
		this.MAT_AFE_groupBox.Text = "AFE Stuff";
		this.label1.AutoSize = true;
		this.label1.Location = new System.Drawing.Point(6, 18);
		this.label1.Name = "label1";
		this.label1.Size = new System.Drawing.Size(71, 13);
		this.label1.TabIndex = 49;
		this.label1.Text = "AFE Mat SEL";
		this.AFE_MAT_Sel_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.AFE_MAT_Sel_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.AFE_MAT_Sel_comboBox.FormattingEnabled = true;
		this.AFE_MAT_Sel_comboBox.Items.AddRange(new object[5] { "Current MAT", "MAT 01", "MAT 03", "MAT 09", "MAT 11" });
		this.AFE_MAT_Sel_comboBox.Location = new System.Drawing.Point(80, 15);
		this.AFE_MAT_Sel_comboBox.Name = "AFE_MAT_Sel_comboBox";
		this.AFE_MAT_Sel_comboBox.Size = new System.Drawing.Size(99, 21);
		this.AFE_MAT_Sel_comboBox.TabIndex = 48;
		this.SEL_VINJ_MUX_Low_label.AutoSize = true;
		this.SEL_VINJ_MUX_Low_label.Location = new System.Drawing.Point(6, 121);
		this.SEL_VINJ_MUX_Low_label.Name = "SEL_VINJ_MUX_Low_label";
		this.SEL_VINJ_MUX_Low_label.Size = new System.Drawing.Size(98, 13);
		this.SEL_VINJ_MUX_Low_label.TabIndex = 47;
		this.SEL_VINJ_MUX_Low_label.Text = "SEL_VINJ_MUX_L";
		this.EN_P_VTH_chkBox.AutoSize = true;
		this.EN_P_VTH_chkBox.Location = new System.Drawing.Point(88, 81);
		this.EN_P_VTH_chkBox.Name = "EN_P_VTH_chkBox";
		this.EN_P_VTH_chkBox.Size = new System.Drawing.Size(82, 17);
		this.EN_P_VTH_chkBox.TabIndex = 41;
		this.EN_P_VTH_chkBox.Text = "EN_P_VTH";
		this.EN_P_VTH_chkBox.UseVisualStyleBackColor = true;
		this.EN_P_VTH_chkBox.CheckedChanged += new System.EventHandler(MAT_AFE_change);
		this.EXT_DC_chkBox.AutoSize = true;
		this.EXT_DC_chkBox.Location = new System.Drawing.Point(6, 81);
		this.EXT_DC_chkBox.Name = "EXT_DC_chkBox";
		this.EXT_DC_chkBox.Size = new System.Drawing.Size(68, 17);
		this.EXT_DC_chkBox.TabIndex = 45;
		this.EXT_DC_chkBox.Text = "EXT_DC";
		this.EXT_DC_chkBox.UseVisualStyleBackColor = true;
		this.EXT_DC_chkBox.CheckedChanged += new System.EventHandler(MAT_AFE_change);
		this.AFE_LB_chkBox.AutoSize = true;
		this.AFE_LB_chkBox.Location = new System.Drawing.Point(6, 67);
		this.AFE_LB_chkBox.Name = "AFE_LB_chkBox";
		this.AFE_LB_chkBox.Size = new System.Drawing.Size(65, 17);
		this.AFE_LB_chkBox.TabIndex = 44;
		this.AFE_LB_chkBox.Text = "AFE_LB";
		this.AFE_LB_chkBox.UseVisualStyleBackColor = true;
		this.AFE_LB_chkBox.CheckedChanged += new System.EventHandler(MAT_AFE_change);
		this.AFE_AUTO_chkBox.AutoSize = true;
		this.AFE_AUTO_chkBox.Location = new System.Drawing.Point(6, 53);
		this.AFE_AUTO_chkBox.Name = "AFE_AUTO_chkBox";
		this.AFE_AUTO_chkBox.Size = new System.Drawing.Size(82, 17);
		this.AFE_AUTO_chkBox.TabIndex = 43;
		this.AFE_AUTO_chkBox.Text = "AFE_AUTO";
		this.AFE_AUTO_chkBox.UseVisualStyleBackColor = true;
		this.AFE_AUTO_chkBox.CheckedChanged += new System.EventHandler(MAT_AFE_change);
		this.EN_P_VLDO_chkBox.AutoSize = true;
		this.EN_P_VLDO_chkBox.Location = new System.Drawing.Point(88, 67);
		this.EN_P_VLDO_chkBox.Name = "EN_P_VLDO_chkBox";
		this.EN_P_VLDO_chkBox.Size = new System.Drawing.Size(89, 17);
		this.EN_P_VLDO_chkBox.TabIndex = 40;
		this.EN_P_VLDO_chkBox.Text = "EN_P_VLDO";
		this.EN_P_VLDO_chkBox.UseVisualStyleBackColor = true;
		this.EN_P_VLDO_chkBox.CheckedChanged += new System.EventHandler(MAT_AFE_change);
		this.CON_PAD_chkBox.AutoSize = true;
		this.CON_PAD_chkBox.Location = new System.Drawing.Point(6, 39);
		this.CON_PAD_chkBox.Name = "CON_PAD_chkBox";
		this.CON_PAD_chkBox.Size = new System.Drawing.Size(77, 17);
		this.CON_PAD_chkBox.TabIndex = 42;
		this.CON_PAD_chkBox.Text = "CON_PAD";
		this.CON_PAD_chkBox.UseVisualStyleBackColor = true;
		this.CON_PAD_chkBox.CheckedChanged += new System.EventHandler(MAT_AFE_change);
		this.EN_P_VFB_chkBox.AutoSize = true;
		this.EN_P_VFB_chkBox.Location = new System.Drawing.Point(88, 53);
		this.EN_P_VFB_chkBox.Name = "EN_P_VFB_chkBox";
		this.EN_P_VFB_chkBox.Size = new System.Drawing.Size(80, 17);
		this.EN_P_VFB_chkBox.TabIndex = 39;
		this.EN_P_VFB_chkBox.Text = "EN_P_VFB";
		this.EN_P_VFB_chkBox.UseVisualStyleBackColor = true;
		this.EN_P_VFB_chkBox.CheckedChanged += new System.EventHandler(MAT_AFE_change);
		this.EN_P_VINJ_chkBox.AutoSize = true;
		this.EN_P_VINJ_chkBox.Location = new System.Drawing.Point(88, 39);
		this.EN_P_VINJ_chkBox.Name = "EN_P_VINJ_chkBox";
		this.EN_P_VINJ_chkBox.Size = new System.Drawing.Size(77, 17);
		this.EN_P_VINJ_chkBox.TabIndex = 38;
		this.EN_P_VINJ_chkBox.Text = "EN_P_Vinj";
		this.EN_P_VINJ_chkBox.UseVisualStyleBackColor = true;
		this.EN_P_VINJ_chkBox.CheckedChanged += new System.EventHandler(MAT_AFE_change);
		this.SEL_VINJ_MUX_High_label.AutoSize = true;
		this.SEL_VINJ_MUX_High_label.Location = new System.Drawing.Point(6, 101);
		this.SEL_VINJ_MUX_High_label.Name = "SEL_VINJ_MUX_High_label";
		this.SEL_VINJ_MUX_High_label.Size = new System.Drawing.Size(100, 13);
		this.SEL_VINJ_MUX_High_label.TabIndex = 32;
		this.SEL_VINJ_MUX_High_label.Text = "SEL_VINJ_MUX_H";
		this.PIX_groupBox.BackColor = System.Drawing.Color.LightGreen;
		this.PIX_groupBox.Controls.Add(this.MAT_DCO0_Period_textBox);
		this.PIX_groupBox.Controls.Add(this.MAT_DCO1_Period_textBox);
		this.PIX_groupBox.Controls.Add(this.MAT_DCO_Difference_textBox);
		this.PIX_groupBox.Controls.Add(this.MAT_DCO_Period_label);
		this.PIX_groupBox.Controls.Add(this.MAT_FE_ON_chkBox);
		this.PIX_groupBox.Controls.Add(this.PIX_ON_chkBox);
		this.PIX_groupBox.Controls.Add(this.PIX_DCO_label);
		this.PIX_groupBox.Controls.Add(this.MAT_FE_ALL_chkBox);
		this.PIX_groupBox.Controls.Add(this.PIX_DCO_ALL_chkBox);
		this.PIX_groupBox.Controls.Add(this.DCO_PIX_adj_UpDown);
		this.PIX_groupBox.Controls.Add(this.DCO_PIX_ctrl_UpDown);
		this.PIX_groupBox.Controls.Add(this.adj_ctrl_1_label);
		this.PIX_groupBox.Controls.Add(this.PIX_ON_ALL_chkBox);
		this.PIX_groupBox.Controls.Add(this.PIX_Sel_UpDown);
		this.PIX_groupBox.Controls.Add(this.PIX_Sel_label);
		this.PIX_groupBox.Location = new System.Drawing.Point(545, 5);
		this.PIX_groupBox.Name = "PIX_groupBox";
		this.PIX_groupBox.Size = new System.Drawing.Size(200, 135);
		this.PIX_groupBox.TabIndex = 17;
		this.PIX_groupBox.TabStop = false;
		this.PIX_groupBox.Text = "PIXEL Controls";
		this.MAT_DCO0_Period_textBox.BackColor = System.Drawing.Color.White;
		this.MAT_DCO0_Period_textBox.Location = new System.Drawing.Point(120, 73);
		this.MAT_DCO0_Period_textBox.Name = "MAT_DCO0_Period_textBox";
		this.MAT_DCO0_Period_textBox.ReadOnly = true;
		this.MAT_DCO0_Period_textBox.Size = new System.Drawing.Size(75, 20);
		this.MAT_DCO0_Period_textBox.TabIndex = 41;
		this.MAT_DCO1_Period_textBox.BackColor = System.Drawing.Color.White;
		this.MAT_DCO1_Period_textBox.Location = new System.Drawing.Point(120, 92);
		this.MAT_DCO1_Period_textBox.Name = "MAT_DCO1_Period_textBox";
		this.MAT_DCO1_Period_textBox.ReadOnly = true;
		this.MAT_DCO1_Period_textBox.Size = new System.Drawing.Size(75, 20);
		this.MAT_DCO1_Period_textBox.TabIndex = 40;
		this.MAT_DCO_Difference_textBox.BackColor = System.Drawing.Color.White;
		this.MAT_DCO_Difference_textBox.Location = new System.Drawing.Point(120, 111);
		this.MAT_DCO_Difference_textBox.Name = "MAT_DCO_Difference_textBox";
		this.MAT_DCO_Difference_textBox.ReadOnly = true;
		this.MAT_DCO_Difference_textBox.Size = new System.Drawing.Size(75, 20);
		this.MAT_DCO_Difference_textBox.TabIndex = 39;
		this.MAT_DCO_Period_label.AutoSize = true;
		this.MAT_DCO_Period_label.Font = new System.Drawing.Font("Microsoft Sans Serif", 9f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.MAT_DCO_Period_label.Location = new System.Drawing.Point(101, 77);
		this.MAT_DCO_Period_label.Name = "MAT_DCO_Period_label";
		this.MAT_DCO_Period_label.Size = new System.Drawing.Size(21, 45);
		this.MAT_DCO_Period_label.TabIndex = 42;
		this.MAT_DCO_Period_label.Text = "T0\r\nT1\r\nR";
		this.PIX_DCO_label.AutoSize = true;
		this.PIX_DCO_label.Location = new System.Drawing.Point(6, 76);
		this.PIX_DCO_label.Name = "PIX_DCO_label";
		this.PIX_DCO_label.Size = new System.Drawing.Size(50, 13);
		this.PIX_DCO_label.TabIndex = 32;
		this.PIX_DCO_label.Text = "PIX DCO";
		this.adj_ctrl_1_label.AutoSize = true;
		this.adj_ctrl_1_label.Location = new System.Drawing.Point(6, 87);
		this.adj_ctrl_1_label.Name = "adj_ctrl_1_label";
		this.adj_ctrl_1_label.Size = new System.Drawing.Size(58, 13);
		this.adj_ctrl_1_label.TabIndex = 28;
		this.adj_ctrl_1_label.Text = "Adj       Ctrl";
		this.PIX_Sel_label.AutoSize = true;
		this.PIX_Sel_label.Location = new System.Drawing.Point(5, 18);
		this.PIX_Sel_label.Name = "PIX_Sel_label";
		this.PIX_Sel_label.Size = new System.Drawing.Size(62, 13);
		this.PIX_Sel_label.TabIndex = 24;
		this.PIX_Sel_label.Text = "Pixel Select";
		this.MAT_DAC_groupBox.BackColor = System.Drawing.Color.Violet;
		this.MAT_DAC_groupBox.Controls.Add(this.DAC_Ikrum_label);
		this.MAT_DAC_groupBox.Controls.Add(this.DAC_ICSA_label);
		this.MAT_DAC_groupBox.Controls.Add(this.DAC_IDISC_label);
		this.MAT_DAC_groupBox.Controls.Add(this.DAC_IKRUM_UpDown);
		this.MAT_DAC_groupBox.Controls.Add(this.DAC_ICSA_UpDown);
		this.MAT_DAC_groupBox.Controls.Add(this.DAC_IDISC_UpDown);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_VFB_UpDown);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_VFB_EN_chkBox);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_VLDO_UpDown);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_VLDO_EN_chkBox);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_VINJ_L_UpDown);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_VINJ_L_EN_chkBox);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_VINJ_H_UpDown);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_VINJ_H_EN_chkBox);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_VTH_L_UpDown);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_VTH_L_EN_chkBox);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_FT_label);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_FT_UpDown);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_VTH_H_UpDown);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_ALL_FT_chkBox);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_FT_SEL_UpDown);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_FT_SEL_label);
		this.MAT_DAC_groupBox.Controls.Add(this.MAT_DAC_VTH_H_EN_chkBox);
		this.MAT_DAC_groupBox.Location = new System.Drawing.Point(777, 151);
		this.MAT_DAC_groupBox.Name = "MAT_DAC_groupBox";
		this.MAT_DAC_groupBox.Size = new System.Drawing.Size(161, 255);
		this.MAT_DAC_groupBox.TabIndex = 24;
		this.MAT_DAC_groupBox.TabStop = false;
		this.MAT_DAC_groupBox.Text = "DAC Controls";
		this.DAC_Ikrum_label.AutoSize = true;
		this.DAC_Ikrum_label.Location = new System.Drawing.Point(13, 231);
		this.DAC_Ikrum_label.Name = "DAC_Ikrum_label";
		this.DAC_Ikrum_label.Size = new System.Drawing.Size(65, 13);
		this.DAC_Ikrum_label.TabIndex = 55;
		this.DAC_Ikrum_label.Text = "DAC I_Krum";
		this.DAC_ICSA_label.AutoSize = true;
		this.DAC_ICSA_label.Location = new System.Drawing.Point(13, 212);
		this.DAC_ICSA_label.Name = "DAC_ICSA_label";
		this.DAC_ICSA_label.Size = new System.Drawing.Size(62, 13);
		this.DAC_ICSA_label.TabIndex = 54;
		this.DAC_ICSA_label.Text = "DAC I_CSA";
		this.DAC_IDISC_label.AutoSize = true;
		this.DAC_IDISC_label.Location = new System.Drawing.Point(13, 193);
		this.DAC_IDISC_label.Name = "DAC_IDISC_label";
		this.DAC_IDISC_label.Size = new System.Drawing.Size(62, 13);
		this.DAC_IDISC_label.TabIndex = 53;
		this.DAC_IDISC_label.Text = "DAC I_Disc";
		this.MAT_DAC_FT_label.AutoSize = true;
		this.MAT_DAC_FT_label.Location = new System.Drawing.Point(1, 59);
		this.MAT_DAC_FT_label.Name = "MAT_DAC_FT_label";
		this.MAT_DAC_FT_label.Size = new System.Drawing.Size(75, 13);
		this.MAT_DAC_FT_label.TabIndex = 39;
		this.MAT_DAC_FT_label.Text = "FT DAC Value";
		this.MAT_DAC_FT_SEL_label.AutoSize = true;
		this.MAT_DAC_FT_SEL_label.Location = new System.Drawing.Point(1, 39);
		this.MAT_DAC_FT_SEL_label.Name = "MAT_DAC_FT_SEL_label";
		this.MAT_DAC_FT_SEL_label.Size = new System.Drawing.Size(78, 13);
		this.MAT_DAC_FT_SEL_label.TabIndex = 24;
		this.MAT_DAC_FT_SEL_label.Text = "FT DAC Select";
		this.MAT_cfg_groupBox.BackColor = System.Drawing.Color.LightGreen;
		this.MAT_cfg_groupBox.Controls.Add(this.SEL_CAL_TIME_UpDown);
		this.MAT_cfg_groupBox.Controls.Add(this.CH_MODE_41_comboBox);
		this.MAT_cfg_groupBox.Controls.Add(this.CH_MODE_42_comboBox);
		this.MAT_cfg_groupBox.Controls.Add(this.CH_MODE_label);
		this.MAT_cfg_groupBox.Controls.Add(this.CH_SEL_41_UpDown);
		this.MAT_cfg_groupBox.Controls.Add(this.CH_SEL_42_UpDown);
		this.MAT_cfg_groupBox.Controls.Add(this.CAL_TIME_label);
		this.MAT_cfg_groupBox.Controls.Add(this.TDC_ON_chkBox);
		this.MAT_cfg_groupBox.Controls.Add(this.EN_TIMEOUT_chkBox);
		this.MAT_cfg_groupBox.Controls.Add(this.enDEtot_chkBox);
		this.MAT_cfg_groupBox.Controls.Add(this.CAL_MODE_chkBox);
		this.MAT_cfg_groupBox.Controls.Add(this.DCO0ctrl_UpDown);
		this.MAT_cfg_groupBox.Controls.Add(this.DCO0adj_UpDown);
		this.MAT_cfg_groupBox.Controls.Add(this.DCO0_MatTab_label);
		this.MAT_cfg_groupBox.Controls.Add(this.DCO0_adj_ctr_MatTab_label);
		this.MAT_cfg_groupBox.Location = new System.Drawing.Point(545, 140);
		this.MAT_cfg_groupBox.Name = "MAT_cfg_groupBox";
		this.MAT_cfg_groupBox.Size = new System.Drawing.Size(200, 162);
		this.MAT_cfg_groupBox.TabIndex = 18;
		this.MAT_cfg_groupBox.TabStop = false;
		this.MAT_cfg_groupBox.Text = "Configuration and Test";
		this.CH_MODE_label.AutoSize = true;
		this.CH_MODE_label.Location = new System.Drawing.Point(4, 116);
		this.CH_MODE_label.Name = "CH_MODE_label";
		this.CH_MODE_label.Size = new System.Drawing.Size(45, 39);
		this.CH_MODE_label.TabIndex = 45;
		this.CH_MODE_label.Text = "TEST\r\nMODE\r\nSettings";
		this.CAL_TIME_label.AutoSize = true;
		this.CAL_TIME_label.Location = new System.Drawing.Point(115, 58);
		this.CAL_TIME_label.Name = "CAL_TIME_label";
		this.CAL_TIME_label.Size = new System.Drawing.Size(51, 13);
		this.CAL_TIME_label.TabIndex = 7;
		this.CAL_TIME_label.Text = "Cal. Time";
		this.DCO0_MatTab_label.AutoSize = true;
		this.DCO0_MatTab_label.Location = new System.Drawing.Point(6, 88);
		this.DCO0_MatTab_label.Name = "DCO0_MatTab_label";
		this.DCO0_MatTab_label.Size = new System.Drawing.Size(72, 13);
		this.DCO0_MatTab_label.TabIndex = 29;
		this.DCO0_MatTab_label.Text = "Global DCO 0";
		this.DCO0_adj_ctr_MatTab_label.AutoSize = true;
		this.DCO0_adj_ctr_MatTab_label.Location = new System.Drawing.Point(77, 72);
		this.DCO0_adj_ctr_MatTab_label.Name = "DCO0_adj_ctr_MatTab_label";
		this.DCO0_adj_ctr_MatTab_label.Size = new System.Drawing.Size(58, 13);
		this.DCO0_adj_ctr_MatTab_label.TabIndex = 29;
		this.DCO0_adj_ctr_MatTab_label.Text = "Adj       Ctrl";
		this.MAT_COMMANDS_groupBox.BackColor = System.Drawing.Color.LightCyan;
		this.MAT_COMMANDS_groupBox.Controls.Add(this.MAT_DCO_GROUP_4863_chkBox);
		this.MAT_COMMANDS_groupBox.Controls.Add(this.MAT_DCO_GROUP_3247_chkBox);
		this.MAT_COMMANDS_groupBox.Controls.Add(this.MAT_DCO_GROUP_1631_chkBox);
		this.MAT_COMMANDS_groupBox.Controls.Add(this.MAT_DCO_GROUP_0015_chkBox);
		this.MAT_COMMANDS_groupBox.Controls.Add(this.CAL_SEL_DCO_comboBox);
		this.MAT_COMMANDS_groupBox.Controls.Add(this.DAQreset_but);
		this.MAT_COMMANDS_groupBox.Controls.Add(this.DCOcalib_but);
		this.MAT_COMMANDS_groupBox.Controls.Add(this.MAT_DCO_GROUPS_label);
		this.MAT_COMMANDS_groupBox.Controls.Add(this.MAT_CAL_SEL_DCO_label);
		this.MAT_COMMANDS_groupBox.Location = new System.Drawing.Point(540, 302);
		this.MAT_COMMANDS_groupBox.Name = "MAT_COMMANDS_groupBox";
		this.MAT_COMMANDS_groupBox.Size = new System.Drawing.Size(232, 103);
		this.MAT_COMMANDS_groupBox.TabIndex = 16;
		this.MAT_COMMANDS_groupBox.TabStop = false;
		this.MAT_COMMANDS_groupBox.Text = "Commands";
		this.MAT_DCO_GROUP_4863_chkBox.AutoSize = true;
		this.MAT_DCO_GROUP_4863_chkBox.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.MAT_DCO_GROUP_4863_chkBox.Location = new System.Drawing.Point(186, 39);
		this.MAT_DCO_GROUP_4863_chkBox.Name = "MAT_DCO_GROUP_4863_chkBox";
		this.MAT_DCO_GROUP_4863_chkBox.Size = new System.Drawing.Size(44, 31);
		this.MAT_DCO_GROUP_4863_chkBox.TabIndex = 39;
		this.MAT_DCO_GROUP_4863_chkBox.Text = "48 - 63";
		this.MAT_DCO_GROUP_4863_chkBox.UseVisualStyleBackColor = true;
		this.MAT_DCO_GROUP_3247_chkBox.AutoSize = true;
		this.MAT_DCO_GROUP_3247_chkBox.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.MAT_DCO_GROUP_3247_chkBox.Location = new System.Drawing.Point(146, 39);
		this.MAT_DCO_GROUP_3247_chkBox.Name = "MAT_DCO_GROUP_3247_chkBox";
		this.MAT_DCO_GROUP_3247_chkBox.Size = new System.Drawing.Size(44, 31);
		this.MAT_DCO_GROUP_3247_chkBox.TabIndex = 38;
		this.MAT_DCO_GROUP_3247_chkBox.Text = "32 - 47";
		this.MAT_DCO_GROUP_3247_chkBox.UseVisualStyleBackColor = true;
		this.MAT_DCO_GROUP_1631_chkBox.AutoSize = true;
		this.MAT_DCO_GROUP_1631_chkBox.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.MAT_DCO_GROUP_1631_chkBox.Location = new System.Drawing.Point(102, 39);
		this.MAT_DCO_GROUP_1631_chkBox.Name = "MAT_DCO_GROUP_1631_chkBox";
		this.MAT_DCO_GROUP_1631_chkBox.Size = new System.Drawing.Size(44, 31);
		this.MAT_DCO_GROUP_1631_chkBox.TabIndex = 37;
		this.MAT_DCO_GROUP_1631_chkBox.Text = "16 - 31";
		this.MAT_DCO_GROUP_1631_chkBox.UseVisualStyleBackColor = true;
		this.MAT_DCO_GROUP_0015_chkBox.AutoSize = true;
		this.MAT_DCO_GROUP_0015_chkBox.CheckAlign = System.Drawing.ContentAlignment.BottomCenter;
		this.MAT_DCO_GROUP_0015_chkBox.Location = new System.Drawing.Point(62, 39);
		this.MAT_DCO_GROUP_0015_chkBox.Name = "MAT_DCO_GROUP_0015_chkBox";
		this.MAT_DCO_GROUP_0015_chkBox.Size = new System.Drawing.Size(44, 31);
		this.MAT_DCO_GROUP_0015_chkBox.TabIndex = 36;
		this.MAT_DCO_GROUP_0015_chkBox.Text = "00 - 15";
		this.MAT_DCO_GROUP_0015_chkBox.UseVisualStyleBackColor = true;
		this.DAQreset_but.Location = new System.Drawing.Point(7, 72);
		this.DAQreset_but.Name = "DAQreset_but";
		this.DAQreset_but.Size = new System.Drawing.Size(66, 25);
		this.DAQreset_but.TabIndex = 4;
		this.DAQreset_but.Text = "DAQ reset";
		this.DAQreset_but.UseVisualStyleBackColor = true;
		this.DAQreset_but.Click += new System.EventHandler(MAT_DCO_COMMAND_but_Click);
		this.DCOcalib_but.ContextMenuStrip = this.TDCcalibAll_contMenuStrip;
		this.DCOcalib_but.Location = new System.Drawing.Point(75, 72);
		this.DCOcalib_but.Name = "DCOcalib_but";
		this.DCOcalib_but.Size = new System.Drawing.Size(142, 25);
		this.DCOcalib_but.TabIndex = 1;
		this.DCOcalib_but.Text = "DCO Group Calibration";
		this.DCOcalib_but.UseVisualStyleBackColor = true;
		this.DCOcalib_but.Click += new System.EventHandler(MAT_DCO_COMMAND_but_Click);
		this.MAT_DCO_GROUPS_label.AutoSize = true;
		this.MAT_DCO_GROUPS_label.Location = new System.Drawing.Point(13, 43);
		this.MAT_DCO_GROUPS_label.Name = "MAT_DCO_GROUPS_label";
		this.MAT_DCO_GROUPS_label.Size = new System.Drawing.Size(53, 26);
		this.MAT_DCO_GROUPS_label.TabIndex = 22;
		this.MAT_DCO_GROUPS_label.Text = "DCO\r\nGROUPS";
		this.MAT_CAL_SEL_DCO_label.AutoSize = true;
		this.MAT_CAL_SEL_DCO_label.Location = new System.Drawing.Point(13, 20);
		this.MAT_CAL_SEL_DCO_label.Name = "MAT_CAL_SEL_DCO_label";
		this.MAT_CAL_SEL_DCO_label.Size = new System.Drawing.Size(119, 13);
		this.MAT_CAL_SEL_DCO_label.TabIndex = 40;
		this.MAT_CAL_SEL_DCO_label.Text = "Calibrate Selected DCO";
		this.MatI2C_read_all_but.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.MatI2C_read_all_but.Location = new System.Drawing.Point(464, 6);
		this.MatI2C_read_all_but.Name = "MatI2C_read_all_but";
		this.MatI2C_read_all_but.Size = new System.Drawing.Size(75, 25);
		this.MatI2C_read_all_but.TabIndex = 14;
		this.MatI2C_read_all_but.Text = "Read All";
		this.MatI2C_read_all_but.UseVisualStyleBackColor = true;
		this.MatI2C_read_all_but.Click += new System.EventHandler(MatI2C_read_all_but_Click);
		this.MatI2C_write_all_but.ContextMenuStrip = this.TDCwriteAll_contMenuStrip;
		this.MatI2C_write_all_but.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.MatI2C_write_all_but.Location = new System.Drawing.Point(383, 6);
		this.MatI2C_write_all_but.Name = "MatI2C_write_all_but";
		this.MatI2C_write_all_but.Size = new System.Drawing.Size(75, 25);
		this.MatI2C_write_all_but.TabIndex = 13;
		this.MatI2C_write_all_but.Text = "Write All";
		this.MatI2C_write_all_but.UseVisualStyleBackColor = true;
		this.MatI2C_write_all_but.Click += new System.EventHandler(MatI2C_write_all_but_Click);
		this.MatI2C_read_single_but.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.MatI2C_read_single_but.Location = new System.Drawing.Point(282, 6);
		this.MatI2C_read_single_but.Name = "MatI2C_read_single_but";
		this.MatI2C_read_single_but.Size = new System.Drawing.Size(75, 25);
		this.MatI2C_read_single_but.TabIndex = 12;
		this.MatI2C_read_single_but.Text = "Read Single";
		this.MatI2C_read_single_but.UseVisualStyleBackColor = true;
		this.MatI2C_read_single_but.Click += new System.EventHandler(MatI2C_read_single_but_Click);
		this.MatI2C_write_single_but.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.MatI2C_write_single_but.Location = new System.Drawing.Point(201, 6);
		this.MatI2C_write_single_but.Name = "MatI2C_write_single_but";
		this.MatI2C_write_single_but.Size = new System.Drawing.Size(75, 25);
		this.MatI2C_write_single_but.TabIndex = 11;
		this.MatI2C_write_single_but.Text = "Write Single";
		this.MatI2C_write_single_but.UseVisualStyleBackColor = true;
		this.MatI2C_write_single_but.Click += new System.EventHandler(MatI2C_write_single_but_Click);
		this.Mat_dGridView.AllowUserToAddRows = false;
		dataGridViewCellStyle.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
		dataGridViewCellStyle.BackColor = System.Drawing.SystemColors.Control;
		dataGridViewCellStyle.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		dataGridViewCellStyle.ForeColor = System.Drawing.SystemColors.WindowText;
		dataGridViewCellStyle.SelectionBackColor = System.Drawing.SystemColors.Highlight;
		dataGridViewCellStyle.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
		dataGridViewCellStyle.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
		this.Mat_dGridView.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle;
		this.Mat_dGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
		this.Mat_dGridView.Columns.AddRange(this.Row_Col1, this.Row_Col2, this.Row_Col3, this.Row_Col4, this.Row_Col5, this.Row_Col6, this.Row_Col7, this.Row_Col8, this.Row_Col9, this.Row_Col10, this.Row_Col11, this.Row_Col12, this.Row_Col13, this.Row_Col14, this.Row_Col15, this.Row_Col16);
		dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
		dataGridViewCellStyle2.BackColor = System.Drawing.SystemColors.Window;
		dataGridViewCellStyle2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		dataGridViewCellStyle2.ForeColor = System.Drawing.SystemColors.ControlText;
		dataGridViewCellStyle2.SelectionBackColor = System.Drawing.SystemColors.Highlight;
		dataGridViewCellStyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
		dataGridViewCellStyle2.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
		this.Mat_dGridView.DefaultCellStyle = dataGridViewCellStyle2;
		this.Mat_dGridView.Location = new System.Drawing.Point(6, 34);
		this.Mat_dGridView.Name = "Mat_dGridView";
		this.Mat_dGridView.RowHeadersWidth = 40;
		this.Mat_dGridView.Size = new System.Drawing.Size(533, 249);
		this.Mat_dGridView.TabIndex = 10;
		this.Row_Col1.HeaderText = "00";
		this.Row_Col1.MinimumWidth = 6;
		this.Row_Col1.Name = "Row_Col1";
		this.Row_Col1.Width = 30;
		this.Row_Col2.HeaderText = "01";
		this.Row_Col2.MinimumWidth = 6;
		this.Row_Col2.Name = "Row_Col2";
		this.Row_Col2.Width = 30;
		this.Row_Col3.HeaderText = "02";
		this.Row_Col3.MinimumWidth = 6;
		this.Row_Col3.Name = "Row_Col3";
		this.Row_Col3.Width = 30;
		this.Row_Col4.HeaderText = "03";
		this.Row_Col4.MinimumWidth = 6;
		this.Row_Col4.Name = "Row_Col4";
		this.Row_Col4.Width = 30;
		this.Row_Col5.HeaderText = "04";
		this.Row_Col5.MinimumWidth = 6;
		this.Row_Col5.Name = "Row_Col5";
		this.Row_Col5.Width = 30;
		this.Row_Col6.HeaderText = "05";
		this.Row_Col6.MinimumWidth = 6;
		this.Row_Col6.Name = "Row_Col6";
		this.Row_Col6.Width = 30;
		this.Row_Col7.HeaderText = "06";
		this.Row_Col7.MinimumWidth = 6;
		this.Row_Col7.Name = "Row_Col7";
		this.Row_Col7.Width = 30;
		this.Row_Col8.HeaderText = "07";
		this.Row_Col8.MinimumWidth = 6;
		this.Row_Col8.Name = "Row_Col8";
		this.Row_Col8.Width = 30;
		this.Row_Col9.HeaderText = "08";
		this.Row_Col9.MinimumWidth = 6;
		this.Row_Col9.Name = "Row_Col9";
		this.Row_Col9.Width = 30;
		this.Row_Col10.HeaderText = "09";
		this.Row_Col10.MinimumWidth = 6;
		this.Row_Col10.Name = "Row_Col10";
		this.Row_Col10.Width = 30;
		this.Row_Col11.HeaderText = "0A";
		this.Row_Col11.MinimumWidth = 6;
		this.Row_Col11.Name = "Row_Col11";
		this.Row_Col11.Width = 30;
		this.Row_Col12.HeaderText = "0B";
		this.Row_Col12.MinimumWidth = 6;
		this.Row_Col12.Name = "Row_Col12";
		this.Row_Col12.Width = 30;
		this.Row_Col13.HeaderText = "0C";
		this.Row_Col13.MinimumWidth = 6;
		this.Row_Col13.Name = "Row_Col13";
		this.Row_Col13.Width = 30;
		this.Row_Col14.HeaderText = "0D";
		this.Row_Col14.MinimumWidth = 6;
		this.Row_Col14.Name = "Row_Col14";
		this.Row_Col14.Width = 30;
		this.Row_Col15.HeaderText = "0E";
		this.Row_Col15.MinimumWidth = 6;
		this.Row_Col15.Name = "Row_Col15";
		this.Row_Col15.Width = 30;
		this.Row_Col16.HeaderText = "0F";
		this.Row_Col16.MinimumWidth = 6;
		this.Row_Col16.Name = "Row_Col16";
		this.Row_Col16.Width = 30;
		this.MatAddr_comboBox.BackColor = System.Drawing.SystemColors.Window;
		this.MatAddr_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.MatAddr_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.MatAddr_comboBox.FormattingEnabled = true;
		this.MatAddr_comboBox.Items.AddRange(new object[18]
		{
			"MAT_00 I2C Address", "MAT_01 I2C Address", "MAT_02 I2C Address", "MAT_03 I2C Address", "MAT_04 I2C Address", "MAT_05 I2C Address", "MAT_06 I2C Address", "MAT_07 I2C Address", "MAT_08 I2C Address", "MAT_09 I2C Address",
			"MAT_10 I2C Address", "MAT_11 I2C Address", "MAT_12 I2C Address", "MAT_13 I2C Address", "MAT_14 I2C Address", "MAT_15 I2C Address", "MAT_BROADCAST", "CUSTOM"
		});
		this.MatAddr_comboBox.Location = new System.Drawing.Point(40, 9);
		this.MatAddr_comboBox.MaxDropDownItems = 7;
		this.MatAddr_comboBox.Name = "MatAddr_comboBox";
		this.MatAddr_comboBox.Size = new System.Drawing.Size(145, 21);
		this.MatAddr_comboBox.TabIndex = 9;
		this.MatAddr_comboBox.SelectedIndexChanged += new System.EventHandler(MatAddr_comboBox_SelectedIndexChanged);
		this.MAT_I2C_addr_tBox.Location = new System.Drawing.Point(9, 8);
		this.MAT_I2C_addr_tBox.Name = "MAT_I2C_addr_tBox";
		this.MAT_I2C_addr_tBox.Size = new System.Drawing.Size(25, 20);
		this.MAT_I2C_addr_tBox.TabIndex = 5;
		this.MAT_I2C_addr_tBox.Text = "AE";
		this.Top_tabPage.BackColor = System.Drawing.Color.LightGray;
		this.Top_tabPage.Controls.Add(this.TopSelQuad_SE_chkBox);
		this.Top_tabPage.Controls.Add(this.TopSelQuad_NE_chkBox);
		this.Top_tabPage.Controls.Add(this.TopSelQuad_SW_chkBox);
		this.Top_tabPage.Controls.Add(this.TopSelQuad_NW_chkBox);
		this.Top_tabPage.Controls.Add(this.DaqFifoForm_but);
		this.Top_tabPage.Controls.Add(this.TOP_COMMANDS_groupBox);
		this.Top_tabPage.Controls.Add(this.CAP_MEAS_groupBox);
		this.Top_tabPage.Controls.Add(this.AFE_PULSE_groupBox);
		this.Top_tabPage.Controls.Add(this.GPO_OUT_SEL_groupBox);
		this.Top_tabPage.Controls.Add(this.TDC_PULSE_groupBox);
		this.Top_tabPage.Controls.Add(this.BXID_groupBox);
		this.Top_tabPage.Controls.Add(this.IOSetSel_groupBox);
		this.Top_tabPage.Controls.Add(this.TopAddr_comboBox);
		this.Top_tabPage.Controls.Add(this.TopI2C_read_all_but);
		this.Top_tabPage.Controls.Add(this.TopI2C_write_all_but);
		this.Top_tabPage.Controls.Add(this.TopI2C_read_single_but);
		this.Top_tabPage.Controls.Add(this.TopI2C_write_single_but);
		this.Top_tabPage.Controls.Add(this.TOP_I2C_addr_tBox);
		this.Top_tabPage.Controls.Add(this.Top_dGridView);
		this.Top_tabPage.Controls.Add(this.DEF_CONFIG_groupBox);
		this.Top_tabPage.Location = new System.Drawing.Point(4, 25);
		this.Top_tabPage.Name = "Top_tabPage";
		this.Top_tabPage.Padding = new System.Windows.Forms.Padding(3);
		this.Top_tabPage.Size = new System.Drawing.Size(942, 410);
		this.Top_tabPage.TabIndex = 0;
		this.Top_tabPage.Text = "TOP";
		this.TopSelQuad_SE_chkBox.AutoSize = true;
		this.TopSelQuad_SE_chkBox.Location = new System.Drawing.Point(50, 390);
		this.TopSelQuad_SE_chkBox.Name = "TopSelQuad_SE_chkBox";
		this.TopSelQuad_SE_chkBox.Size = new System.Drawing.Size(40, 17);
		this.TopSelQuad_SE_chkBox.TabIndex = 73;
		this.TopSelQuad_SE_chkBox.Text = "SE";
		this.TopSelQuad_SE_chkBox.UseVisualStyleBackColor = true;
		this.TopSelQuad_SE_chkBox.CheckedChanged += new System.EventHandler(Top_Quad_Sel_Change);
		this.TopSelQuad_NE_chkBox.AutoSize = true;
		this.TopSelQuad_NE_chkBox.Location = new System.Drawing.Point(50, 368);
		this.TopSelQuad_NE_chkBox.Name = "TopSelQuad_NE_chkBox";
		this.TopSelQuad_NE_chkBox.Size = new System.Drawing.Size(41, 17);
		this.TopSelQuad_NE_chkBox.TabIndex = 72;
		this.TopSelQuad_NE_chkBox.Text = "NE";
		this.TopSelQuad_NE_chkBox.UseVisualStyleBackColor = true;
		this.TopSelQuad_NE_chkBox.CheckedChanged += new System.EventHandler(Top_Quad_Sel_Change);
		this.TopSelQuad_SW_chkBox.AutoSize = true;
		this.TopSelQuad_SW_chkBox.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.TopSelQuad_SW_chkBox.Location = new System.Drawing.Point(3, 390);
		this.TopSelQuad_SW_chkBox.Name = "TopSelQuad_SW_chkBox";
		this.TopSelQuad_SW_chkBox.Size = new System.Drawing.Size(44, 17);
		this.TopSelQuad_SW_chkBox.TabIndex = 71;
		this.TopSelQuad_SW_chkBox.Text = "SW";
		this.TopSelQuad_SW_chkBox.UseVisualStyleBackColor = true;
		this.TopSelQuad_SW_chkBox.CheckedChanged += new System.EventHandler(Top_Quad_Sel_Change);
		this.TopSelQuad_NW_chkBox.AutoSize = true;
		this.TopSelQuad_NW_chkBox.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.TopSelQuad_NW_chkBox.Location = new System.Drawing.Point(3, 368);
		this.TopSelQuad_NW_chkBox.Name = "TopSelQuad_NW_chkBox";
		this.TopSelQuad_NW_chkBox.Size = new System.Drawing.Size(45, 17);
		this.TopSelQuad_NW_chkBox.TabIndex = 70;
		this.TopSelQuad_NW_chkBox.Text = "NW";
		this.TopSelQuad_NW_chkBox.UseVisualStyleBackColor = true;
		this.TopSelQuad_NW_chkBox.CheckedChanged += new System.EventHandler(Top_Quad_Sel_Change);
		this.DaqFifoForm_but.Location = new System.Drawing.Point(557, 365);
		this.DaqFifoForm_but.Name = "DaqFifoForm_but";
		this.DaqFifoForm_but.Size = new System.Drawing.Size(75, 39);
		this.DaqFifoForm_but.TabIndex = 69;
		this.DaqFifoForm_but.Text = "Open DAQ FIFO form";
		this.DaqFifoForm_but.UseVisualStyleBackColor = true;
		this.DaqFifoForm_but.Click += new System.EventHandler(DaqFifoForm_but_click);
		this.TOP_COMMANDS_groupBox.BackColor = System.Drawing.Color.LightGreen;
		this.TOP_COMMANDS_groupBox.Controls.Add(this.TOP_COMM_START_CAL_chkBox);
		this.TOP_COMMANDS_groupBox.Controls.Add(this.TOP_TDCpulse_but);
		this.TOP_COMMANDS_groupBox.Controls.Add(this.TOP_DAQreset_but);
		this.TOP_COMMANDS_groupBox.Controls.Add(this.TOP_COMM_START_AUTO_chkBox);
		this.TOP_COMMANDS_groupBox.Controls.Add(this.TOP_COMM_FRC_RST_CAL_chkBox);
		this.TOP_COMMANDS_groupBox.Location = new System.Drawing.Point(694, 321);
		this.TOP_COMMANDS_groupBox.Name = "TOP_COMMANDS_groupBox";
		this.TOP_COMMANDS_groupBox.Size = new System.Drawing.Size(217, 75);
		this.TOP_COMMANDS_groupBox.TabIndex = 68;
		this.TOP_COMMANDS_groupBox.TabStop = false;
		this.TOP_COMMANDS_groupBox.Text = "TOP Commands";
		this.TOP_COMM_START_CAL_chkBox.AutoSize = true;
		this.TOP_COMM_START_CAL_chkBox.Location = new System.Drawing.Point(9, 22);
		this.TOP_COMM_START_CAL_chkBox.Name = "TOP_COMM_START_CAL_chkBox";
		this.TOP_COMM_START_CAL_chkBox.Size = new System.Drawing.Size(100, 17);
		this.TOP_COMM_START_CAL_chkBox.TabIndex = 65;
		this.TOP_COMM_START_CAL_chkBox.Text = "Start Calibration";
		this.TOP_COMM_START_CAL_chkBox.UseVisualStyleBackColor = true;
		this.TOP_COMM_START_CAL_chkBox.CheckedChanged += new System.EventHandler(TOP_COMMANDS_but_Click);
		this.TOP_TDCpulse_but.ContextMenuStrip = this.TOP_TDCpulse_contextMenuStrip;
		this.TOP_TDCpulse_but.Location = new System.Drawing.Point(114, 20);
		this.TOP_TDCpulse_but.Name = "TOP_TDCpulse_but";
		this.TOP_TDCpulse_but.Size = new System.Drawing.Size(98, 25);
		this.TOP_TDCpulse_but.TabIndex = 64;
		this.TOP_TDCpulse_but.Text = "TDC Test Pulse";
		this.TOP_TDCpulse_but.UseVisualStyleBackColor = true;
		this.TOP_TDCpulse_but.Click += new System.EventHandler(TOP_COMMANDS_but_Click);
		this.TOP_TDCpulse_contextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[5] { this.TOP_TDCpulse_x32times, this.TOP_TDCpulse_x50times, this.TOP_TDCpulse_x64times, this.TOP_TDCpulse_x100times, this.TOP_TDCpulse_x128times });
		this.TOP_TDCpulse_contextMenuStrip.Name = "TOP_TDCpulse_contextMenuStrip";
		this.TOP_TDCpulse_contextMenuStrip.Size = new System.Drawing.Size(155, 114);
		this.TOP_TDCpulse_x32times.Name = "TOP_TDCpulse_x32times";
		this.TOP_TDCpulse_x32times.Size = new System.Drawing.Size(154, 22);
		this.TOP_TDCpulse_x32times.Text = "TDC Pulse x 32";
		this.TOP_TDCpulse_x32times.Click += new System.EventHandler(TDC_PULSE_contMenuStripClick);
		this.TOP_TDCpulse_x50times.Name = "TOP_TDCpulse_x50times";
		this.TOP_TDCpulse_x50times.Size = new System.Drawing.Size(154, 22);
		this.TOP_TDCpulse_x50times.Text = "TDC Pulse x 50";
		this.TOP_TDCpulse_x50times.Click += new System.EventHandler(TDC_PULSE_contMenuStripClick);
		this.TOP_TDCpulse_x64times.Name = "TOP_TDCpulse_x64times";
		this.TOP_TDCpulse_x64times.Size = new System.Drawing.Size(154, 22);
		this.TOP_TDCpulse_x64times.Text = "TDC Pulse x 64";
		this.TOP_TDCpulse_x64times.Click += new System.EventHandler(TDC_PULSE_contMenuStripClick);
		this.TOP_TDCpulse_x100times.Name = "TOP_TDCpulse_x100times";
		this.TOP_TDCpulse_x100times.Size = new System.Drawing.Size(154, 22);
		this.TOP_TDCpulse_x100times.Text = "TDC Pulse x100";
		this.TOP_TDCpulse_x100times.Click += new System.EventHandler(TDC_PULSE_contMenuStripClick);
		this.TOP_TDCpulse_x128times.Name = "TOP_TDCpulse_x128times";
		this.TOP_TDCpulse_x128times.Size = new System.Drawing.Size(154, 22);
		this.TOP_TDCpulse_x128times.Text = "TDC Pulse x128";
		this.TOP_TDCpulse_x128times.Click += new System.EventHandler(TDC_PULSE_contMenuStripClick);
		this.TOP_DAQreset_but.Location = new System.Drawing.Point(114, 45);
		this.TOP_DAQreset_but.Name = "TOP_DAQreset_but";
		this.TOP_DAQreset_but.Size = new System.Drawing.Size(98, 25);
		this.TOP_DAQreset_but.TabIndex = 63;
		this.TOP_DAQreset_but.Text = "DAQ reset";
		this.TOP_DAQreset_but.UseVisualStyleBackColor = true;
		this.TOP_DAQreset_but.Click += new System.EventHandler(TOP_COMMANDS_but_Click);
		this.TOP_COMM_START_AUTO_chkBox.AutoSize = true;
		this.TOP_COMM_START_AUTO_chkBox.Location = new System.Drawing.Point(9, 38);
		this.TOP_COMM_START_AUTO_chkBox.Name = "TOP_COMM_START_AUTO_chkBox";
		this.TOP_COMM_START_AUTO_chkBox.Size = new System.Drawing.Size(102, 17);
		this.TOP_COMM_START_AUTO_chkBox.TabIndex = 66;
		this.TOP_COMM_START_AUTO_chkBox.Text = "Start Auto Pulse";
		this.TOP_COMM_START_AUTO_chkBox.UseVisualStyleBackColor = true;
		this.TOP_COMM_START_AUTO_chkBox.CheckedChanged += new System.EventHandler(TOP_COMMANDS_but_Click);
		this.TOP_COMM_FRC_RST_CAL_chkBox.AutoSize = true;
		this.TOP_COMM_FRC_RST_CAL_chkBox.Location = new System.Drawing.Point(9, 54);
		this.TOP_COMM_FRC_RST_CAL_chkBox.Name = "TOP_COMM_FRC_RST_CAL_chkBox";
		this.TOP_COMM_FRC_RST_CAL_chkBox.Size = new System.Drawing.Size(111, 17);
		this.TOP_COMM_FRC_RST_CAL_chkBox.TabIndex = 67;
		this.TOP_COMM_FRC_RST_CAL_chkBox.Text = "Force Restart Cal.";
		this.TOP_COMM_FRC_RST_CAL_chkBox.UseVisualStyleBackColor = true;
		this.TOP_COMM_FRC_RST_CAL_chkBox.CheckedChanged += new System.EventHandler(TOP_COMMANDS_but_Click);
		this.CAP_MEAS_groupBox.BackColor = System.Drawing.Color.Pink;
		this.CAP_MEAS_groupBox.Controls.Add(this.CMES_CC_04F_UpDown);
		this.CAP_MEAS_groupBox.Controls.Add(this.CMES_CC_04F_label);
		this.CAP_MEAS_groupBox.Controls.Add(this.CMES_CQ_20F_UpDown);
		this.CAP_MEAS_groupBox.Controls.Add(this.CMES_CQ_20F_label);
		this.CAP_MEAS_groupBox.Controls.Add(this.CMES_CF_20F_UpDown);
		this.CAP_MEAS_groupBox.Controls.Add(this.CMES_CF_20F_label);
		this.CAP_MEAS_groupBox.Controls.Add(this.CMES_DPOL_comboBox);
		this.CAP_MEAS_groupBox.Controls.Add(this.CMES_SEL_WAIT_UpDown);
		this.CAP_MEAS_groupBox.Controls.Add(this.CMES_SEL_WAIT_label);
		this.CAP_MEAS_groupBox.Controls.Add(this.CMES_ARST_chkBox);
		this.CAP_MEAS_groupBox.Controls.Add(this.CMES_QPOL_comboBox);
		this.CAP_MEAS_groupBox.Controls.Add(this.CMES_DPOL_label);
		this.CAP_MEAS_groupBox.Controls.Add(this.CMES_QPOL_label);
		this.CAP_MEAS_groupBox.Controls.Add(this.CMES_DEN_chkBox);
		this.CAP_MEAS_groupBox.Controls.Add(this.CMES_AEN_chkBox);
		this.CAP_MEAS_groupBox.Location = new System.Drawing.Point(686, 200);
		this.CAP_MEAS_groupBox.Name = "CAP_MEAS_groupBox";
		this.CAP_MEAS_groupBox.Size = new System.Drawing.Size(251, 115);
		this.CAP_MEAS_groupBox.TabIndex = 61;
		this.CAP_MEAS_groupBox.TabStop = false;
		this.CAP_MEAS_groupBox.Text = "Cap. Meas. Settings";
		this.CMES_CC_04F_UpDown.Location = new System.Drawing.Point(122, 92);
		this.CMES_CC_04F_UpDown.Maximum = new decimal(new int[4] { 15, 0, 0, 0 });
		this.CMES_CC_04F_UpDown.Name = "CMES_CC_04F_UpDown";
		this.CMES_CC_04F_UpDown.Size = new System.Drawing.Size(47, 20);
		this.CMES_CC_04F_UpDown.TabIndex = 59;
		this.CMES_CC_04F_UpDown.ValueChanged += new System.EventHandler(CAP_MEAS_Change);
		this.CMES_CC_04F_label.AutoSize = true;
		this.CMES_CC_04F_label.Location = new System.Drawing.Point(166, 95);
		this.CMES_CC_04F_label.Name = "CMES_CC_04F_label";
		this.CMES_CC_04F_label.Size = new System.Drawing.Size(71, 13);
		this.CMES_CC_04F_label.TabIndex = 60;
		this.CMES_CC_04F_label.Text = "Cmes CC 04F";
		this.CMES_CQ_20F_UpDown.Location = new System.Drawing.Point(122, 73);
		this.CMES_CQ_20F_UpDown.Maximum = new decimal(new int[4] { 7, 0, 0, 0 });
		this.CMES_CQ_20F_UpDown.Name = "CMES_CQ_20F_UpDown";
		this.CMES_CQ_20F_UpDown.Size = new System.Drawing.Size(47, 20);
		this.CMES_CQ_20F_UpDown.TabIndex = 57;
		this.CMES_CQ_20F_UpDown.ValueChanged += new System.EventHandler(CAP_MEAS_Change);
		this.CMES_CQ_20F_label.AutoSize = true;
		this.CMES_CQ_20F_label.Location = new System.Drawing.Point(166, 76);
		this.CMES_CQ_20F_label.Name = "CMES_CQ_20F_label";
		this.CMES_CQ_20F_label.Size = new System.Drawing.Size(72, 13);
		this.CMES_CQ_20F_label.TabIndex = 58;
		this.CMES_CQ_20F_label.Text = "Cmes CQ 20F";
		this.CMES_CF_20F_UpDown.Location = new System.Drawing.Point(122, 54);
		this.CMES_CF_20F_UpDown.Maximum = new decimal(new int[4] { 7, 0, 0, 0 });
		this.CMES_CF_20F_UpDown.Name = "CMES_CF_20F_UpDown";
		this.CMES_CF_20F_UpDown.Size = new System.Drawing.Size(47, 20);
		this.CMES_CF_20F_UpDown.TabIndex = 55;
		this.CMES_CF_20F_UpDown.ValueChanged += new System.EventHandler(CAP_MEAS_Change);
		this.CMES_CF_20F_label.AutoSize = true;
		this.CMES_CF_20F_label.Location = new System.Drawing.Point(166, 57);
		this.CMES_CF_20F_label.Name = "CMES_CF_20F_label";
		this.CMES_CF_20F_label.Size = new System.Drawing.Size(70, 13);
		this.CMES_CF_20F_label.TabIndex = 56;
		this.CMES_CF_20F_label.Text = "Cmes CF 20F";
		this.CMES_DPOL_comboBox.BackColor = System.Drawing.Color.White;
		this.CMES_DPOL_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.CMES_DPOL_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.CMES_DPOL_comboBox.FormattingEnabled = true;
		this.CMES_DPOL_comboBox.Items.AddRange(new object[2] { "TOCHECK NEG", "TOCHECK POS" });
		this.CMES_DPOL_comboBox.Location = new System.Drawing.Point(162, 31);
		this.CMES_DPOL_comboBox.MaxDropDownItems = 2;
		this.CMES_DPOL_comboBox.Name = "CMES_DPOL_comboBox";
		this.CMES_DPOL_comboBox.Size = new System.Drawing.Size(80, 21);
		this.CMES_DPOL_comboBox.TabIndex = 53;
		this.CMES_DPOL_comboBox.SelectedIndexChanged += new System.EventHandler(CAP_MEAS_Change);
		this.CMES_SEL_WAIT_UpDown.Location = new System.Drawing.Point(14, 66);
		this.CMES_SEL_WAIT_UpDown.Maximum = new decimal(new int[4] { 3, 0, 0, 0 });
		this.CMES_SEL_WAIT_UpDown.Name = "CMES_SEL_WAIT_UpDown";
		this.CMES_SEL_WAIT_UpDown.Size = new System.Drawing.Size(47, 20);
		this.CMES_SEL_WAIT_UpDown.TabIndex = 49;
		this.CMES_SEL_WAIT_UpDown.ValueChanged += new System.EventHandler(CAP_MEAS_Change);
		this.CMES_SEL_WAIT_label.AutoSize = true;
		this.CMES_SEL_WAIT_label.Location = new System.Drawing.Point(58, 69);
		this.CMES_SEL_WAIT_label.Name = "CMES_SEL_WAIT_label";
		this.CMES_SEL_WAIT_label.Size = new System.Drawing.Size(58, 13);
		this.CMES_SEL_WAIT_label.TabIndex = 50;
		this.CMES_SEL_WAIT_label.Text = "Cmes Wait";
		this.CMES_ARST_chkBox.AutoSize = true;
		this.CMES_ARST_chkBox.Location = new System.Drawing.Point(14, 50);
		this.CMES_ARST_chkBox.Name = "CMES_ARST_chkBox";
		this.CMES_ARST_chkBox.Size = new System.Drawing.Size(93, 17);
		this.CMES_ARST_chkBox.TabIndex = 52;
		this.CMES_ARST_chkBox.Text = "Cmes A Reset";
		this.CMES_ARST_chkBox.UseVisualStyleBackColor = true;
		this.CMES_ARST_chkBox.CheckedChanged += new System.EventHandler(CAP_MEAS_Change);
		this.CMES_QPOL_comboBox.BackColor = System.Drawing.Color.White;
		this.CMES_QPOL_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.CMES_QPOL_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.CMES_QPOL_comboBox.FormattingEnabled = true;
		this.CMES_QPOL_comboBox.Items.AddRange(new object[2] { "TOCHECK NEG", "TOCHECK POS" });
		this.CMES_QPOL_comboBox.Location = new System.Drawing.Point(162, 9);
		this.CMES_QPOL_comboBox.MaxDropDownItems = 2;
		this.CMES_QPOL_comboBox.Name = "CMES_QPOL_comboBox";
		this.CMES_QPOL_comboBox.Size = new System.Drawing.Size(80, 21);
		this.CMES_QPOL_comboBox.TabIndex = 19;
		this.CMES_QPOL_comboBox.SelectedIndexChanged += new System.EventHandler(CAP_MEAS_Change);
		this.CMES_DPOL_label.AutoSize = true;
		this.CMES_DPOL_label.Location = new System.Drawing.Point(96, 35);
		this.CMES_DPOL_label.Name = "CMES_DPOL_label";
		this.CMES_DPOL_label.Size = new System.Drawing.Size(68, 13);
		this.CMES_DPOL_label.TabIndex = 54;
		this.CMES_DPOL_label.Text = "Cmes D POL";
		this.CMES_QPOL_label.AutoSize = true;
		this.CMES_QPOL_label.Location = new System.Drawing.Point(96, 13);
		this.CMES_QPOL_label.Name = "CMES_QPOL_label";
		this.CMES_QPOL_label.Size = new System.Drawing.Size(68, 13);
		this.CMES_QPOL_label.TabIndex = 20;
		this.CMES_QPOL_label.Text = "Cmes Q POL";
		this.CMES_DEN_chkBox.AutoSize = true;
		this.CMES_DEN_chkBox.Location = new System.Drawing.Point(14, 32);
		this.CMES_DEN_chkBox.Name = "CMES_DEN_chkBox";
		this.CMES_DEN_chkBox.Size = new System.Drawing.Size(86, 17);
		this.CMES_DEN_chkBox.TabIndex = 51;
		this.CMES_DEN_chkBox.Text = "Cmes DEN  |";
		this.CMES_DEN_chkBox.UseVisualStyleBackColor = true;
		this.CMES_DEN_chkBox.CheckedChanged += new System.EventHandler(CAP_MEAS_Change);
		this.CMES_AEN_chkBox.AutoSize = true;
		this.CMES_AEN_chkBox.Location = new System.Drawing.Point(14, 13);
		this.CMES_AEN_chkBox.Name = "CMES_AEN_chkBox";
		this.CMES_AEN_chkBox.Size = new System.Drawing.Size(85, 17);
		this.CMES_AEN_chkBox.TabIndex = 50;
		this.CMES_AEN_chkBox.Text = "Cmes AEN  |";
		this.CMES_AEN_chkBox.UseVisualStyleBackColor = true;
		this.CMES_AEN_chkBox.CheckedChanged += new System.EventHandler(CAP_MEAS_Change);
		this.AFE_PULSE_groupBox.BackColor = System.Drawing.Color.Violet;
		this.AFE_PULSE_groupBox.Controls.Add(this.AFE_EOS_chkBox);
		this.AFE_PULSE_groupBox.Controls.Add(this.AFE_TP_REPETITION_UpDown);
		this.AFE_PULSE_groupBox.Controls.Add(this.AFE_TP_REPETITION_label);
		this.AFE_PULSE_groupBox.Controls.Add(this.AFE_TP_WIDTH_UpDown);
		this.AFE_PULSE_groupBox.Controls.Add(this.AFE_TP_WIDTH_label);
		this.AFE_PULSE_groupBox.Controls.Add(this.AFE_TP_PERIOD_UpDown);
		this.AFE_PULSE_groupBox.Controls.Add(this.AFE_TP_PERIOD_label);
		this.AFE_PULSE_groupBox.Controls.Add(this.AFE_EN_TP_PHASE_UpDown);
		this.AFE_PULSE_groupBox.Controls.Add(this.AFE_LISTEN_TIME_UpDown);
		this.AFE_PULSE_groupBox.Controls.Add(this.AFE_UPDATE_TIME_UpDown);
		this.AFE_PULSE_groupBox.Controls.Add(this.AFE_EN_TP_PHASE_label);
		this.AFE_PULSE_groupBox.Controls.Add(this.AFE_LISTEN_TIME_label);
		this.AFE_PULSE_groupBox.Controls.Add(this.AFE_UPDATE_TIME_label);
		this.AFE_PULSE_groupBox.Controls.Add(this.AFE_START_TP_chkBox);
		this.AFE_PULSE_groupBox.Location = new System.Drawing.Point(540, 200);
		this.AFE_PULSE_groupBox.Name = "AFE_PULSE_groupBox";
		this.AFE_PULSE_groupBox.Size = new System.Drawing.Size(148, 150);
		this.AFE_PULSE_groupBox.TabIndex = 49;
		this.AFE_PULSE_groupBox.TabStop = false;
		this.AFE_PULSE_groupBox.Text = "AFE Pulsing";
		this.AFE_EOS_chkBox.AutoCheck = false;
		this.AFE_EOS_chkBox.AutoSize = true;
		this.AFE_EOS_chkBox.Enabled = false;
		this.AFE_EOS_chkBox.Location = new System.Drawing.Point(70, 129);
		this.AFE_EOS_chkBox.Name = "AFE_EOS_chkBox";
		this.AFE_EOS_chkBox.Size = new System.Drawing.Size(71, 17);
		this.AFE_EOS_chkBox.TabIndex = 48;
		this.AFE_EOS_chkBox.Text = "AFE EOS";
		this.AFE_EOS_chkBox.UseVisualStyleBackColor = true;
		this.AFE_TP_REPETITION_UpDown.Location = new System.Drawing.Point(7, 108);
		this.AFE_TP_REPETITION_UpDown.Maximum = new decimal(new int[4] { 63, 0, 0, 0 });
		this.AFE_TP_REPETITION_UpDown.Name = "AFE_TP_REPETITION_UpDown";
		this.AFE_TP_REPETITION_UpDown.Size = new System.Drawing.Size(47, 20);
		this.AFE_TP_REPETITION_UpDown.TabIndex = 45;
		this.AFE_TP_REPETITION_UpDown.ValueChanged += new System.EventHandler(AFE_PULSE_Change);
		this.AFE_TP_REPETITION_label.AutoSize = true;
		this.AFE_TP_REPETITION_label.Location = new System.Drawing.Point(51, 111);
		this.AFE_TP_REPETITION_label.Name = "AFE_TP_REPETITION_label";
		this.AFE_TP_REPETITION_label.Size = new System.Drawing.Size(72, 13);
		this.AFE_TP_REPETITION_label.TabIndex = 46;
		this.AFE_TP_REPETITION_label.Text = "TP Repetition";
		this.AFE_TP_WIDTH_UpDown.Location = new System.Drawing.Point(7, 89);
		this.AFE_TP_WIDTH_UpDown.Maximum = new decimal(new int[4] { 7, 0, 0, 0 });
		this.AFE_TP_WIDTH_UpDown.Name = "AFE_TP_WIDTH_UpDown";
		this.AFE_TP_WIDTH_UpDown.Size = new System.Drawing.Size(47, 20);
		this.AFE_TP_WIDTH_UpDown.TabIndex = 43;
		this.AFE_TP_WIDTH_UpDown.ValueChanged += new System.EventHandler(AFE_PULSE_Change);
		this.AFE_TP_WIDTH_label.AutoSize = true;
		this.AFE_TP_WIDTH_label.Location = new System.Drawing.Point(51, 92);
		this.AFE_TP_WIDTH_label.Name = "AFE_TP_WIDTH_label";
		this.AFE_TP_WIDTH_label.Size = new System.Drawing.Size(81, 13);
		this.AFE_TP_WIDTH_label.TabIndex = 44;
		this.AFE_TP_WIDTH_label.Text = "TP Width100ns";
		this.AFE_TP_PERIOD_UpDown.Location = new System.Drawing.Point(7, 70);
		this.AFE_TP_PERIOD_UpDown.Maximum = new decimal(new int[4] { 15, 0, 0, 0 });
		this.AFE_TP_PERIOD_UpDown.Name = "AFE_TP_PERIOD_UpDown";
		this.AFE_TP_PERIOD_UpDown.Size = new System.Drawing.Size(47, 20);
		this.AFE_TP_PERIOD_UpDown.TabIndex = 41;
		this.AFE_TP_PERIOD_UpDown.ValueChanged += new System.EventHandler(AFE_PULSE_Change);
		this.AFE_TP_PERIOD_label.AutoSize = true;
		this.AFE_TP_PERIOD_label.Location = new System.Drawing.Point(51, 73);
		this.AFE_TP_PERIOD_label.Name = "AFE_TP_PERIOD_label";
		this.AFE_TP_PERIOD_label.Size = new System.Drawing.Size(83, 13);
		this.AFE_TP_PERIOD_label.TabIndex = 42;
		this.AFE_TP_PERIOD_label.Text = "TP Period100ns";
		this.AFE_EN_TP_PHASE_UpDown.Location = new System.Drawing.Point(7, 51);
		this.AFE_EN_TP_PHASE_UpDown.Maximum = new decimal(new int[4] { 3, 0, 0, 0 });
		this.AFE_EN_TP_PHASE_UpDown.Name = "AFE_EN_TP_PHASE_UpDown";
		this.AFE_EN_TP_PHASE_UpDown.Size = new System.Drawing.Size(47, 20);
		this.AFE_EN_TP_PHASE_UpDown.TabIndex = 39;
		this.AFE_EN_TP_PHASE_UpDown.ValueChanged += new System.EventHandler(AFE_PULSE_Change);
		this.AFE_LISTEN_TIME_UpDown.Location = new System.Drawing.Point(7, 32);
		this.AFE_LISTEN_TIME_UpDown.Maximum = new decimal(new int[4] { 63, 0, 0, 0 });
		this.AFE_LISTEN_TIME_UpDown.Name = "AFE_LISTEN_TIME_UpDown";
		this.AFE_LISTEN_TIME_UpDown.Size = new System.Drawing.Size(47, 20);
		this.AFE_LISTEN_TIME_UpDown.TabIndex = 37;
		this.AFE_LISTEN_TIME_UpDown.ValueChanged += new System.EventHandler(AFE_PULSE_Change);
		this.AFE_UPDATE_TIME_UpDown.Location = new System.Drawing.Point(7, 13);
		this.AFE_UPDATE_TIME_UpDown.Maximum = new decimal(new int[4] { 255, 0, 0, 0 });
		this.AFE_UPDATE_TIME_UpDown.Name = "AFE_UPDATE_TIME_UpDown";
		this.AFE_UPDATE_TIME_UpDown.Size = new System.Drawing.Size(47, 20);
		this.AFE_UPDATE_TIME_UpDown.TabIndex = 19;
		this.AFE_UPDATE_TIME_UpDown.ValueChanged += new System.EventHandler(AFE_PULSE_Change);
		this.AFE_EN_TP_PHASE_label.AutoSize = true;
		this.AFE_EN_TP_PHASE_label.Location = new System.Drawing.Point(51, 54);
		this.AFE_EN_TP_PHASE_label.Name = "AFE_EN_TP_PHASE_label";
		this.AFE_EN_TP_PHASE_label.Size = new System.Drawing.Size(96, 13);
		this.AFE_EN_TP_PHASE_label.TabIndex = 40;
		this.AFE_EN_TP_PHASE_label.Text = "EnTP Phase100ns";
		this.AFE_LISTEN_TIME_label.AutoSize = true;
		this.AFE_LISTEN_TIME_label.Location = new System.Drawing.Point(51, 35);
		this.AFE_LISTEN_TIME_label.Name = "AFE_LISTEN_TIME_label";
		this.AFE_LISTEN_TIME_label.Size = new System.Drawing.Size(87, 13);
		this.AFE_LISTEN_TIME_label.TabIndex = 38;
		this.AFE_LISTEN_TIME_label.Text = "ListenTime200ns";
		this.AFE_UPDATE_TIME_label.AutoSize = true;
		this.AFE_UPDATE_TIME_label.Location = new System.Drawing.Point(51, 16);
		this.AFE_UPDATE_TIME_label.Name = "AFE_UPDATE_TIME_label";
		this.AFE_UPDATE_TIME_label.Size = new System.Drawing.Size(94, 13);
		this.AFE_UPDATE_TIME_label.TabIndex = 20;
		this.AFE_UPDATE_TIME_label.Text = "UpdateTime200ns";
		this.AFE_START_TP_chkBox.AutoSize = true;
		this.AFE_START_TP_chkBox.Location = new System.Drawing.Point(7, 129);
		this.AFE_START_TP_chkBox.Name = "AFE_START_TP_chkBox";
		this.AFE_START_TP_chkBox.Size = new System.Drawing.Size(65, 17);
		this.AFE_START_TP_chkBox.TabIndex = 47;
		this.AFE_START_TP_chkBox.Text = "Start TP";
		this.AFE_START_TP_chkBox.UseVisualStyleBackColor = true;
		this.AFE_START_TP_chkBox.CheckedChanged += new System.EventHandler(AFE_PULSE_Change);
		this.GPO_OUT_SEL_groupBox.BackColor = System.Drawing.Color.Tomato;
		this.GPO_OUT_SEL_groupBox.Controls.Add(this.FIXED_PATTERN_textBox);
		this.GPO_OUT_SEL_groupBox.Controls.Add(this.FIXED_PATTERN_label);
		this.GPO_OUT_SEL_groupBox.Controls.Add(this.FIXED_PATTERN_UpDown);
		this.GPO_OUT_SEL_groupBox.Controls.Add(this.READOUT_SER_label);
		this.GPO_OUT_SEL_groupBox.Controls.Add(this.GPO_SEL_label);
		this.GPO_OUT_SEL_groupBox.Controls.Add(this.SER_CK_SEL_comboBox);
		this.GPO_OUT_SEL_groupBox.Controls.Add(this.SEL_RO_comboBox);
		this.GPO_OUT_SEL_groupBox.Controls.Add(this.GPO_CMOS_SEL_comboBox);
		this.GPO_OUT_SEL_groupBox.Controls.Add(this.GPO_SLVS_SEL_comboBox);
		this.GPO_OUT_SEL_groupBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f);
		this.GPO_OUT_SEL_groupBox.Location = new System.Drawing.Point(686, 110);
		this.GPO_OUT_SEL_groupBox.Name = "GPO_OUT_SEL_groupBox";
		this.GPO_OUT_SEL_groupBox.Size = new System.Drawing.Size(252, 88);
		this.GPO_OUT_SEL_groupBox.TabIndex = 34;
		this.GPO_OUT_SEL_groupBox.TabStop = false;
		this.GPO_OUT_SEL_groupBox.Text = "Readout/GPO";
		this.FIXED_PATTERN_label.AutoSize = true;
		this.FIXED_PATTERN_label.Location = new System.Drawing.Point(201, 12);
		this.FIXED_PATTERN_label.Name = "FIXED_PATTERN_label";
		this.FIXED_PATTERN_label.Size = new System.Drawing.Size(47, 13);
		this.FIXED_PATTERN_label.TabIndex = 23;
		this.FIXED_PATTERN_label.Text = "FIX PAT";
		this.FIXED_PATTERN_UpDown.Location = new System.Drawing.Point(201, 27);
		this.FIXED_PATTERN_UpDown.Maximum = new decimal(new int[4] { 255, 0, 0, 0 });
		this.FIXED_PATTERN_UpDown.Name = "FIXED_PATTERN_UpDown";
		this.FIXED_PATTERN_UpDown.Size = new System.Drawing.Size(47, 20);
		this.FIXED_PATTERN_UpDown.TabIndex = 19;
		this.FIXED_PATTERN_UpDown.ValueChanged += new System.EventHandler(GPO_OUT_SEL_Change);
		this.READOUT_SER_label.AutoSize = true;
		this.READOUT_SER_label.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f);
		this.READOUT_SER_label.Location = new System.Drawing.Point(5, 49);
		this.READOUT_SER_label.Name = "READOUT_SER_label";
		this.READOUT_SER_label.Size = new System.Drawing.Size(183, 13);
		this.READOUT_SER_label.TabIndex = 33;
		this.READOUT_SER_label.Text = "Readout Interface      Serializer Clock";
		this.GPO_SEL_label.AutoSize = true;
		this.GPO_SEL_label.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f);
		this.GPO_SEL_label.Location = new System.Drawing.Point(19, 13);
		this.GPO_SEL_label.Name = "GPO_SEL_label";
		this.GPO_SEL_label.Size = new System.Drawing.Size(153, 13);
		this.GPO_SEL_label.TabIndex = 32;
		this.GPO_SEL_label.Text = "GPO SLVS            GPO CMOS";
		this.SER_CK_SEL_comboBox.BackColor = System.Drawing.Color.White;
		this.SER_CK_SEL_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.SER_CK_SEL_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.SER_CK_SEL_comboBox.FormattingEnabled = true;
		this.SER_CK_SEL_comboBox.Items.AddRange(new object[4] { "640 MHz", "320 MHz", "160 MHz", "640 MHz" });
		this.SER_CK_SEL_comboBox.Location = new System.Drawing.Point(103, 63);
		this.SER_CK_SEL_comboBox.MaxDropDownItems = 4;
		this.SER_CK_SEL_comboBox.Name = "SER_CK_SEL_comboBox";
		this.SER_CK_SEL_comboBox.Size = new System.Drawing.Size(110, 21);
		this.SER_CK_SEL_comboBox.TabIndex = 31;
		this.SER_CK_SEL_comboBox.SelectedIndexChanged += new System.EventHandler(GPO_OUT_SEL_Change);
		this.SEL_RO_comboBox.BackColor = System.Drawing.Color.White;
		this.SEL_RO_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.SEL_RO_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.SEL_RO_comboBox.FormattingEnabled = true;
		this.SEL_RO_comboBox.Items.AddRange(new object[4] { "NONE", "NONE", "I2C Readout", "SER Readout" });
		this.SEL_RO_comboBox.Location = new System.Drawing.Point(4, 63);
		this.SEL_RO_comboBox.MaxDropDownItems = 4;
		this.SEL_RO_comboBox.Name = "SEL_RO_comboBox";
		this.SEL_RO_comboBox.Size = new System.Drawing.Size(92, 21);
		this.SEL_RO_comboBox.TabIndex = 30;
		this.SEL_RO_comboBox.SelectedIndexChanged += new System.EventHandler(GPO_OUT_SEL_Change);
		this.GPO_CMOS_SEL_comboBox.BackColor = System.Drawing.Color.White;
		this.GPO_CMOS_SEL_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.GPO_CMOS_SEL_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.GPO_CMOS_SEL_comboBox.FormattingEnabled = true;
		this.GPO_CMOS_SEL_comboBox.Items.AddRange(new object[16]
		{
			"HitOr", "CLK 40 MHz", "AFE_EOS", "FastIn", "PulseTDC", "noSyncFlag", "NC", "NC", "NC", "NC",
			"NC", "NC", "NC", "NC", "NC", "NC"
		});
		this.GPO_CMOS_SEL_comboBox.Location = new System.Drawing.Point(112, 28);
		this.GPO_CMOS_SEL_comboBox.MaxDropDownItems = 6;
		this.GPO_CMOS_SEL_comboBox.Name = "GPO_CMOS_SEL_comboBox";
		this.GPO_CMOS_SEL_comboBox.Size = new System.Drawing.Size(81, 21);
		this.GPO_CMOS_SEL_comboBox.TabIndex = 29;
		this.GPO_CMOS_SEL_comboBox.SelectedIndexChanged += new System.EventHandler(GPO_OUT_SEL_Change);
		this.GPO_SLVS_SEL_comboBox.BackColor = System.Drawing.Color.White;
		this.GPO_SLVS_SEL_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.GPO_SLVS_SEL_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.GPO_SLVS_SEL_comboBox.FormattingEnabled = true;
		this.GPO_SLVS_SEL_comboBox.Items.AddRange(new object[16]
		{
			"CLK 640 MHz", "HitOr", "CLK 40  MHz", "AFE_EOS", "FastIn", "PulseTDC", "noSyncFlag", "CapMesDiscOut", "CapMesP", "CLK Serializer?",
			"NC", "NC", "NC", "NC", "NC", "NC"
		});
		this.GPO_SLVS_SEL_comboBox.Location = new System.Drawing.Point(4, 28);
		this.GPO_SLVS_SEL_comboBox.MaxDropDownItems = 10;
		this.GPO_SLVS_SEL_comboBox.Name = "GPO_SLVS_SEL_comboBox";
		this.GPO_SLVS_SEL_comboBox.Size = new System.Drawing.Size(102, 21);
		this.GPO_SLVS_SEL_comboBox.TabIndex = 28;
		this.GPO_SLVS_SEL_comboBox.SelectedIndexChanged += new System.EventHandler(GPO_OUT_SEL_Change);
		this.TDC_PULSE_groupBox.BackColor = System.Drawing.Color.Gold;
		this.TDC_PULSE_groupBox.Controls.Add(this.POINT_TA_TOT_label);
		this.TDC_PULSE_groupBox.Controls.Add(this.SEL_PULSE_SRC_label);
		this.TDC_PULSE_groupBox.Controls.Add(this.POINT_TOT_UpDown);
		this.TDC_PULSE_groupBox.Controls.Add(this.POINT_TA_UpDown);
		this.TDC_PULSE_groupBox.Controls.Add(this.SEL_PULSE_SRC_comboBox);
		this.TDC_PULSE_groupBox.Controls.Add(this.TEST_POINT_label);
		this.TDC_PULSE_groupBox.Location = new System.Drawing.Point(543, 107);
		this.TDC_PULSE_groupBox.Name = "TDC_PULSE_groupBox";
		this.TDC_PULSE_groupBox.Size = new System.Drawing.Size(143, 91);
		this.TDC_PULSE_groupBox.TabIndex = 27;
		this.TDC_PULSE_groupBox.TabStop = false;
		this.TDC_PULSE_groupBox.Text = "TDC Pulsing";
		this.POINT_TA_TOT_label.AutoSize = true;
		this.POINT_TA_TOT_label.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		this.POINT_TA_TOT_label.Location = new System.Drawing.Point(48, 53);
		this.POINT_TA_TOT_label.Name = "POINT_TA_TOT_label";
		this.POINT_TA_TOT_label.Size = new System.Drawing.Size(80, 13);
		this.POINT_TA_TOT_label.TabIndex = 26;
		this.POINT_TA_TOT_label.Text = "TA        TOT";
		this.SEL_PULSE_SRC_label.AutoSize = true;
		this.SEL_PULSE_SRC_label.Location = new System.Drawing.Point(41, 15);
		this.SEL_PULSE_SRC_label.Name = "SEL_PULSE_SRC_label";
		this.SEL_PULSE_SRC_label.Size = new System.Drawing.Size(70, 13);
		this.SEL_PULSE_SRC_label.TabIndex = 23;
		this.SEL_PULSE_SRC_label.Text = "Pulse Source";
		this.POINT_TOT_UpDown.Location = new System.Drawing.Point(90, 66);
		this.POINT_TOT_UpDown.Maximum = new decimal(new int[4] { 31, 0, 0, 0 });
		this.POINT_TOT_UpDown.Name = "POINT_TOT_UpDown";
		this.POINT_TOT_UpDown.Size = new System.Drawing.Size(47, 20);
		this.POINT_TOT_UpDown.TabIndex = 24;
		this.POINT_TOT_UpDown.ValueChanged += new System.EventHandler(TDC_PULSE_Change);
		this.POINT_TA_UpDown.Location = new System.Drawing.Point(39, 66);
		this.POINT_TA_UpDown.Maximum = new decimal(new int[4] { 15, 0, 0, 0 });
		this.POINT_TA_UpDown.Name = "POINT_TA_UpDown";
		this.POINT_TA_UpDown.Size = new System.Drawing.Size(47, 20);
		this.POINT_TA_UpDown.TabIndex = 19;
		this.POINT_TA_UpDown.ValueChanged += new System.EventHandler(TDC_PULSE_Change);
		this.SEL_PULSE_SRC_comboBox.BackColor = System.Drawing.Color.White;
		this.SEL_PULSE_SRC_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.SEL_PULSE_SRC_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.SEL_PULSE_SRC_comboBox.FormattingEnabled = true;
		this.SEL_PULSE_SRC_comboBox.Items.AddRange(new object[4] { "NONE", "Internal Pulse", "NONE", "External Pulse" });
		this.SEL_PULSE_SRC_comboBox.Location = new System.Drawing.Point(7, 30);
		this.SEL_PULSE_SRC_comboBox.MaxDropDownItems = 4;
		this.SEL_PULSE_SRC_comboBox.Name = "SEL_PULSE_SRC_comboBox";
		this.SEL_PULSE_SRC_comboBox.Size = new System.Drawing.Size(129, 21);
		this.SEL_PULSE_SRC_comboBox.TabIndex = 19;
		this.SEL_PULSE_SRC_comboBox.SelectedIndexChanged += new System.EventHandler(TDC_PULSE_Change);
		this.TEST_POINT_label.AutoSize = true;
		this.TEST_POINT_label.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
		this.TEST_POINT_label.Location = new System.Drawing.Point(5, 60);
		this.TEST_POINT_label.Name = "TEST_POINT_label";
		this.TEST_POINT_label.Size = new System.Drawing.Size(36, 26);
		this.TEST_POINT_label.TabIndex = 25;
		this.TEST_POINT_label.Text = "Test\r\nPoint";
		this.BXID_groupBox.BackColor = System.Drawing.Color.DodgerBlue;
		this.BXID_groupBox.Controls.Add(this.BXID_LMT_label);
		this.BXID_groupBox.Controls.Add(this.BXID_PRL_label);
		this.BXID_groupBox.Controls.Add(this.BXID_LMT_UpDown);
		this.BXID_groupBox.Controls.Add(this.BXID_PRL_UpDown);
		this.BXID_groupBox.Location = new System.Drawing.Point(543, 46);
		this.BXID_groupBox.Name = "BXID_groupBox";
		this.BXID_groupBox.Size = new System.Drawing.Size(143, 65);
		this.BXID_groupBox.TabIndex = 23;
		this.BXID_groupBox.TabStop = false;
		this.BXID_groupBox.Text = "BXID Config";
		this.BXID_LMT_label.AutoSize = true;
		this.BXID_LMT_label.Location = new System.Drawing.Point(77, 37);
		this.BXID_LMT_label.Name = "BXID_LMT_label";
		this.BXID_LMT_label.Size = new System.Drawing.Size(60, 13);
		this.BXID_LMT_label.TabIndex = 22;
		this.BXID_LMT_label.Text = "BXID_LMT";
		this.BXID_PRL_label.AutoSize = true;
		this.BXID_PRL_label.Location = new System.Drawing.Point(77, 16);
		this.BXID_PRL_label.Name = "BXID_PRL_label";
		this.BXID_PRL_label.Size = new System.Drawing.Size(59, 13);
		this.BXID_PRL_label.TabIndex = 19;
		this.BXID_PRL_label.Text = "BXID_PRL";
		this.BXID_LMT_UpDown.Location = new System.Drawing.Point(7, 35);
		this.BXID_LMT_UpDown.Maximum = new decimal(new int[4] { 4095, 0, 0, 0 });
		this.BXID_LMT_UpDown.Name = "BXID_LMT_UpDown";
		this.BXID_LMT_UpDown.Size = new System.Drawing.Size(64, 20);
		this.BXID_LMT_UpDown.TabIndex = 21;
		this.BXID_LMT_UpDown.Value = new decimal(new int[4] { 3563, 0, 0, 0 });
		this.BXID_LMT_UpDown.ValueChanged += new System.EventHandler(BXID_Change);
		this.BXID_PRL_UpDown.Location = new System.Drawing.Point(7, 14);
		this.BXID_PRL_UpDown.Maximum = new decimal(new int[4] { 4095, 0, 0, 0 });
		this.BXID_PRL_UpDown.Name = "BXID_PRL_UpDown";
		this.BXID_PRL_UpDown.Size = new System.Drawing.Size(64, 20);
		this.BXID_PRL_UpDown.TabIndex = 20;
		this.BXID_PRL_UpDown.ValueChanged += new System.EventHandler(BXID_Change);
		this.IOSetSel_groupBox.BackColor = System.Drawing.Color.Cyan;
		this.IOSetSel_groupBox.Controls.Add(this.FE_POLARITY_label);
		this.IOSetSel_groupBox.Controls.Add(this.SLVS_INVRX_chkBox);
		this.IOSetSel_groupBox.Controls.Add(this.SLVS_TRM_label);
		this.IOSetSel_groupBox.Controls.Add(this.SLVS_DRV_STR_label);
		this.IOSetSel_groupBox.Controls.Add(this.SLVS_CMM_MODE_label);
		this.IOSetSel_groupBox.Controls.Add(this.FASTIN_EN_chkBox);
		this.IOSetSel_groupBox.Controls.Add(this.SLVS_INVTX_chkBox);
		this.IOSetSel_groupBox.Controls.Add(this.FE_POLARITY_comboBox);
		this.IOSetSel_groupBox.Controls.Add(this.SLVS_TRM_comboBox);
		this.IOSetSel_groupBox.Controls.Add(this.SLVS_DRV_STR_UpDown);
		this.IOSetSel_groupBox.Controls.Add(this.SLVS_CMM_MODE_UpDown);
		this.IOSetSel_groupBox.Location = new System.Drawing.Point(686, 6);
		this.IOSetSel_groupBox.Margin = new System.Windows.Forms.Padding(2);
		this.IOSetSel_groupBox.Name = "IOSetSel_groupBox";
		this.IOSetSel_groupBox.Padding = new System.Windows.Forms.Padding(2);
		this.IOSetSel_groupBox.Size = new System.Drawing.Size(252, 103);
		this.IOSetSel_groupBox.TabIndex = 19;
		this.IOSetSel_groupBox.TabStop = false;
		this.IOSetSel_groupBox.Text = "I/O Settings";
		this.FE_POLARITY_label.AutoSize = true;
		this.FE_POLARITY_label.Location = new System.Drawing.Point(0, 82);
		this.FE_POLARITY_label.Name = "FE_POLARITY_label";
		this.FE_POLARITY_label.Size = new System.Drawing.Size(60, 13);
		this.FE_POLARITY_label.TabIndex = 18;
		this.FE_POLARITY_label.Text = "FE_Polarity";
		this.SLVS_INVRX_chkBox.AutoSize = true;
		this.SLVS_INVRX_chkBox.Location = new System.Drawing.Point(160, 35);
		this.SLVS_INVRX_chkBox.Name = "SLVS_INVRX_chkBox";
		this.SLVS_INVRX_chkBox.Size = new System.Drawing.Size(92, 17);
		this.SLVS_INVRX_chkBox.TabIndex = 13;
		this.SLVS_INVRX_chkBox.Text = "SLVS_INVRX";
		this.SLVS_INVRX_chkBox.UseVisualStyleBackColor = true;
		this.SLVS_INVRX_chkBox.CheckedChanged += new System.EventHandler(IOSetSel_Change);
		this.SLVS_TRM_label.AutoSize = true;
		this.SLVS_TRM_label.Location = new System.Drawing.Point(0, 60);
		this.SLVS_TRM_label.Name = "SLVS_TRM_label";
		this.SLVS_TRM_label.Size = new System.Drawing.Size(64, 13);
		this.SLVS_TRM_label.TabIndex = 17;
		this.SLVS_TRM_label.Text = "SLVS_TRM";
		this.SLVS_DRV_STR_label.AutoSize = true;
		this.SLVS_DRV_STR_label.Location = new System.Drawing.Point(52, 36);
		this.SLVS_DRV_STR_label.Name = "SLVS_DRV_STR_label";
		this.SLVS_DRV_STR_label.Size = new System.Drawing.Size(91, 13);
		this.SLVS_DRV_STR_label.TabIndex = 16;
		this.SLVS_DRV_STR_label.Text = "SLVS_DRV_STR";
		this.SLVS_CMM_MODE_label.AutoSize = true;
		this.SLVS_CMM_MODE_label.Location = new System.Drawing.Point(52, 17);
		this.SLVS_CMM_MODE_label.Name = "SLVS_CMM_MODE_label";
		this.SLVS_CMM_MODE_label.Size = new System.Drawing.Size(103, 13);
		this.SLVS_CMM_MODE_label.TabIndex = 15;
		this.SLVS_CMM_MODE_label.Text = "SLVS_CMM_MODE";
		this.FASTIN_EN_chkBox.AutoSize = true;
		this.FASTIN_EN_chkBox.Location = new System.Drawing.Point(160, 53);
		this.FASTIN_EN_chkBox.Name = "FASTIN_EN_chkBox";
		this.FASTIN_EN_chkBox.Size = new System.Drawing.Size(76, 17);
		this.FASTIN_EN_chkBox.TabIndex = 14;
		this.FASTIN_EN_chkBox.Text = "En. FastIN";
		this.FASTIN_EN_chkBox.UseVisualStyleBackColor = true;
		this.FASTIN_EN_chkBox.CheckedChanged += new System.EventHandler(IOSetSel_Change);
		this.SLVS_INVTX_chkBox.AutoSize = true;
		this.SLVS_INVTX_chkBox.Location = new System.Drawing.Point(160, 16);
		this.SLVS_INVTX_chkBox.Name = "SLVS_INVTX_chkBox";
		this.SLVS_INVTX_chkBox.Size = new System.Drawing.Size(91, 17);
		this.SLVS_INVTX_chkBox.TabIndex = 12;
		this.SLVS_INVTX_chkBox.Text = "SLVS_INVTX";
		this.SLVS_INVTX_chkBox.UseVisualStyleBackColor = true;
		this.SLVS_INVTX_chkBox.CheckedChanged += new System.EventHandler(IOSetSel_Change);
		this.FE_POLARITY_comboBox.BackColor = System.Drawing.Color.White;
		this.FE_POLARITY_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.FE_POLARITY_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.FE_POLARITY_comboBox.FormattingEnabled = true;
		this.FE_POLARITY_comboBox.Items.AddRange(new object[2] { "Active Low", "Active High" });
		this.FE_POLARITY_comboBox.Location = new System.Drawing.Point(73, 78);
		this.FE_POLARITY_comboBox.MaxDropDownItems = 2;
		this.FE_POLARITY_comboBox.Name = "FE_POLARITY_comboBox";
		this.FE_POLARITY_comboBox.Size = new System.Drawing.Size(80, 21);
		this.FE_POLARITY_comboBox.TabIndex = 11;
		this.FE_POLARITY_comboBox.SelectedIndexChanged += new System.EventHandler(IOSetSel_Change);
		this.SLVS_TRM_comboBox.BackColor = System.Drawing.Color.White;
		this.SLVS_TRM_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.SLVS_TRM_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.SLVS_TRM_comboBox.FormattingEnabled = true;
		this.SLVS_TRM_comboBox.Items.AddRange(new object[2] { "10 kOhm", "100 Ohm" });
		this.SLVS_TRM_comboBox.Location = new System.Drawing.Point(73, 55);
		this.SLVS_TRM_comboBox.MaxDropDownItems = 2;
		this.SLVS_TRM_comboBox.Name = "SLVS_TRM_comboBox";
		this.SLVS_TRM_comboBox.Size = new System.Drawing.Size(80, 21);
		this.SLVS_TRM_comboBox.TabIndex = 10;
		this.SLVS_TRM_comboBox.SelectedIndexChanged += new System.EventHandler(IOSetSel_Change);
		this.SLVS_DRV_STR_UpDown.Location = new System.Drawing.Point(4, 37);
		this.SLVS_DRV_STR_UpDown.Maximum = new decimal(new int[4] { 15, 0, 0, 0 });
		this.SLVS_DRV_STR_UpDown.Name = "SLVS_DRV_STR_UpDown";
		this.SLVS_DRV_STR_UpDown.Size = new System.Drawing.Size(47, 20);
		this.SLVS_DRV_STR_UpDown.TabIndex = 9;
		this.SLVS_DRV_STR_UpDown.ValueChanged += new System.EventHandler(IOSetSel_Change);
		this.SLVS_CMM_MODE_UpDown.Location = new System.Drawing.Point(4, 16);
		this.SLVS_CMM_MODE_UpDown.Maximum = new decimal(new int[4] { 15, 0, 0, 0 });
		this.SLVS_CMM_MODE_UpDown.Name = "SLVS_CMM_MODE_UpDown";
		this.SLVS_CMM_MODE_UpDown.Size = new System.Drawing.Size(47, 20);
		this.SLVS_CMM_MODE_UpDown.TabIndex = 8;
		this.SLVS_CMM_MODE_UpDown.ValueChanged += new System.EventHandler(IOSetSel_Change);
		this.TopAddr_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.TopAddr_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.TopAddr_comboBox.FormattingEnabled = true;
		this.TopAddr_comboBox.Items.AddRange(new object[3] { "TOP I2C Address", "Prova I2C Address", "Custom I2C Address" });
		this.TopAddr_comboBox.Location = new System.Drawing.Point(40, 9);
		this.TopAddr_comboBox.MaxDropDownItems = 3;
		this.TopAddr_comboBox.Name = "TopAddr_comboBox";
		this.TopAddr_comboBox.Size = new System.Drawing.Size(115, 21);
		this.TopAddr_comboBox.TabIndex = 7;
		this.TopAddr_comboBox.SelectedIndexChanged += new System.EventHandler(TopAddr_comboBox_SelectedIndexChanged);
		this.TopI2C_read_all_but.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.TopI2C_read_all_but.Location = new System.Drawing.Point(464, 6);
		this.TopI2C_read_all_but.Name = "TopI2C_read_all_but";
		this.TopI2C_read_all_but.Size = new System.Drawing.Size(75, 25);
		this.TopI2C_read_all_but.TabIndex = 6;
		this.TopI2C_read_all_but.Text = "Read All";
		this.TopI2C_read_all_but.UseVisualStyleBackColor = true;
		this.TopI2C_read_all_but.Click += new System.EventHandler(TopI2C_read_all_but_Click);
		this.TopI2C_write_all_but.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.TopI2C_write_all_but.Location = new System.Drawing.Point(383, 6);
		this.TopI2C_write_all_but.Name = "TopI2C_write_all_but";
		this.TopI2C_write_all_but.Size = new System.Drawing.Size(75, 25);
		this.TopI2C_write_all_but.TabIndex = 5;
		this.TopI2C_write_all_but.Text = "Write All";
		this.TopI2C_write_all_but.UseVisualStyleBackColor = true;
		this.TopI2C_write_all_but.Click += new System.EventHandler(TopI2C_write_all_but_Click);
		this.TopI2C_read_single_but.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.TopI2C_read_single_but.Location = new System.Drawing.Point(282, 6);
		this.TopI2C_read_single_but.Name = "TopI2C_read_single_but";
		this.TopI2C_read_single_but.Size = new System.Drawing.Size(75, 25);
		this.TopI2C_read_single_but.TabIndex = 4;
		this.TopI2C_read_single_but.Text = "Read Single";
		this.TopI2C_read_single_but.UseVisualStyleBackColor = true;
		this.TopI2C_read_single_but.Click += new System.EventHandler(TopI2C_read_single_but_Click);
		this.TopI2C_write_single_but.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.TopI2C_write_single_but.Location = new System.Drawing.Point(201, 6);
		this.TopI2C_write_single_but.Name = "TopI2C_write_single_but";
		this.TopI2C_write_single_but.Size = new System.Drawing.Size(75, 25);
		this.TopI2C_write_single_but.TabIndex = 3;
		this.TopI2C_write_single_but.Text = "Write Single";
		this.TopI2C_write_single_but.UseVisualStyleBackColor = true;
		this.TopI2C_write_single_but.Click += new System.EventHandler(TopI2C_write_single_but_Click);
		this.TOP_I2C_addr_tBox.Location = new System.Drawing.Point(9, 8);
		this.TOP_I2C_addr_tBox.Name = "TOP_I2C_addr_tBox";
		this.TOP_I2C_addr_tBox.Size = new System.Drawing.Size(25, 20);
		this.TOP_I2C_addr_tBox.TabIndex = 1;
		this.TOP_I2C_addr_tBox.Text = "AE";
		this.Top_dGridView.AllowUserToAddRows = false;
		dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
		dataGridViewCellStyle3.BackColor = System.Drawing.SystemColors.Control;
		dataGridViewCellStyle3.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		dataGridViewCellStyle3.ForeColor = System.Drawing.SystemColors.WindowText;
		dataGridViewCellStyle3.SelectionBackColor = System.Drawing.SystemColors.Highlight;
		dataGridViewCellStyle3.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
		dataGridViewCellStyle3.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
		this.Top_dGridView.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle3;
		this.Top_dGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
		this.Top_dGridView.Columns.AddRange(this.Top_Col1, this.Top_Col2, this.Top_Col3, this.Top_Col4, this.Top_Col5, this.Top_Col6, this.Top_Col7, this.Top_Col8, this.Top_Col9, this.Top_Col10, this.Top_Col11, this.Top_Col12, this.Top_Col13, this.Top_Col14, this.Top_Col15, this.Top_Col16);
		dataGridViewCellStyle4.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
		dataGridViewCellStyle4.BackColor = System.Drawing.SystemColors.Window;
		dataGridViewCellStyle4.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		dataGridViewCellStyle4.ForeColor = System.Drawing.SystemColors.ControlText;
		dataGridViewCellStyle4.SelectionBackColor = System.Drawing.SystemColors.Highlight;
		dataGridViewCellStyle4.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
		dataGridViewCellStyle4.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
		this.Top_dGridView.DefaultCellStyle = dataGridViewCellStyle4;
		this.Top_dGridView.Location = new System.Drawing.Point(6, 34);
		this.Top_dGridView.Name = "Top_dGridView";
		this.Top_dGridView.RowHeadersWidth = 40;
		this.Top_dGridView.Size = new System.Drawing.Size(533, 328);
		this.Top_dGridView.TabIndex = 0;
		this.Top_dGridView.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(Top_dGridView_CellContentClick);
		this.Top_Col1.HeaderText = "00";
		this.Top_Col1.MinimumWidth = 6;
		this.Top_Col1.Name = "Top_Col1";
		this.Top_Col1.Width = 30;
		this.Top_Col2.HeaderText = "01";
		this.Top_Col2.MinimumWidth = 6;
		this.Top_Col2.Name = "Top_Col2";
		this.Top_Col2.Width = 30;
		this.Top_Col3.HeaderText = "02";
		this.Top_Col3.MinimumWidth = 6;
		this.Top_Col3.Name = "Top_Col3";
		this.Top_Col3.Width = 30;
		this.Top_Col4.HeaderText = "03";
		this.Top_Col4.MinimumWidth = 6;
		this.Top_Col4.Name = "Top_Col4";
		this.Top_Col4.Width = 30;
		this.Top_Col5.HeaderText = "04";
		this.Top_Col5.MinimumWidth = 6;
		this.Top_Col5.Name = "Top_Col5";
		this.Top_Col5.Width = 30;
		this.Top_Col6.HeaderText = "05";
		this.Top_Col6.MinimumWidth = 6;
		this.Top_Col6.Name = "Top_Col6";
		this.Top_Col6.Width = 30;
		this.Top_Col7.HeaderText = "06";
		this.Top_Col7.MinimumWidth = 6;
		this.Top_Col7.Name = "Top_Col7";
		this.Top_Col7.Width = 30;
		this.Top_Col8.HeaderText = "07";
		this.Top_Col8.MinimumWidth = 6;
		this.Top_Col8.Name = "Top_Col8";
		this.Top_Col8.Width = 30;
		this.Top_Col9.HeaderText = "08";
		this.Top_Col9.MinimumWidth = 6;
		this.Top_Col9.Name = "Top_Col9";
		this.Top_Col9.Width = 30;
		this.Top_Col10.HeaderText = "09";
		this.Top_Col10.MinimumWidth = 6;
		this.Top_Col10.Name = "Top_Col10";
		this.Top_Col10.Width = 30;
		this.Top_Col11.HeaderText = "0A";
		this.Top_Col11.MinimumWidth = 6;
		this.Top_Col11.Name = "Top_Col11";
		this.Top_Col11.Width = 30;
		this.Top_Col12.HeaderText = "0B";
		this.Top_Col12.MinimumWidth = 6;
		this.Top_Col12.Name = "Top_Col12";
		this.Top_Col12.Width = 30;
		this.Top_Col13.HeaderText = "0C";
		this.Top_Col13.MinimumWidth = 6;
		this.Top_Col13.Name = "Top_Col13";
		this.Top_Col13.Width = 30;
		this.Top_Col14.HeaderText = "0D";
		this.Top_Col14.MinimumWidth = 6;
		this.Top_Col14.Name = "Top_Col14";
		this.Top_Col14.Width = 30;
		this.Top_Col15.HeaderText = "0E";
		this.Top_Col15.MinimumWidth = 6;
		this.Top_Col15.Name = "Top_Col15";
		this.Top_Col15.Width = 30;
		this.Top_Col16.HeaderText = "0F";
		this.Top_Col16.MinimumWidth = 6;
		this.Top_Col16.Name = "Top_Col16";
		this.Top_Col16.Width = 30;
		this.DEF_CONFIG_groupBox.BackColor = System.Drawing.Color.WhiteSmoke;
		this.DEF_CONFIG_groupBox.Controls.Add(this.DEFAULT_CONFIG_chkBox);
		this.DEF_CONFIG_groupBox.Location = new System.Drawing.Point(543, 6);
		this.DEF_CONFIG_groupBox.Name = "DEF_CONFIG_groupBox";
		this.DEF_CONFIG_groupBox.Size = new System.Drawing.Size(143, 43);
		this.DEF_CONFIG_groupBox.TabIndex = 36;
		this.DEF_CONFIG_groupBox.TabStop = false;
		this.DEFAULT_CONFIG_chkBox.AutoSize = true;
		this.DEFAULT_CONFIG_chkBox.Checked = true;
		this.DEFAULT_CONFIG_chkBox.CheckState = System.Windows.Forms.CheckState.Checked;
		this.DEFAULT_CONFIG_chkBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.35f);
		this.DEFAULT_CONFIG_chkBox.Location = new System.Drawing.Point(12, 15);
		this.DEFAULT_CONFIG_chkBox.Name = "DEFAULT_CONFIG_chkBox";
		this.DEFAULT_CONFIG_chkBox.Size = new System.Drawing.Size(118, 19);
		this.DEFAULT_CONFIG_chkBox.TabIndex = 35;
		this.DEFAULT_CONFIG_chkBox.Text = "DEFAULT Config";
		this.DEFAULT_CONFIG_chkBox.UseVisualStyleBackColor = true;
		this.DEFAULT_CONFIG_chkBox.CheckedChanged += new System.EventHandler(DEF_CONFIG_Change);
		this.tabControl1.Appearance = System.Windows.Forms.TabAppearance.Buttons;
		this.tabControl1.Controls.Add(this.Top_tabPage);
		this.tabControl1.Controls.Add(this.Mat_tabPage);
		this.tabControl1.Controls.Add(this.IOext_tabPage);
		this.tabControl1.Controls.Add(this.exAdcDac_tabPage);
		this.tabControl1.Controls.Add(this.Ctrl_tabPage);
		this.tabControl1.Location = new System.Drawing.Point(12, 30);
		this.tabControl1.Name = "tabControl1";
		this.tabControl1.SelectedIndex = 0;
		this.tabControl1.Size = new System.Drawing.Size(950, 439);
		this.tabControl1.TabIndex = 1;
		this.IOext_tabPage.BackColor = System.Drawing.Color.LightGray;
		this.IOext_tabPage.Controls.Add(this.I2Cmux_grpBox);
		this.IOext_tabPage.Controls.Add(this.groupBox6);
		this.IOext_tabPage.Controls.Add(this.IOextI2C_read_all_but);
		this.IOext_tabPage.Controls.Add(this.IOextI2C_write_all_but);
		this.IOext_tabPage.Controls.Add(this.IOextI2C_read_single_but);
		this.IOext_tabPage.Controls.Add(this.IOextI2C_write_single_but);
		this.IOext_tabPage.Controls.Add(this.IOext_dGridView);
		this.IOext_tabPage.Controls.Add(this.IOextAddr_comboBox);
		this.IOext_tabPage.Controls.Add(this.IOext_I2C_addr_tBox);
		this.IOext_tabPage.Location = new System.Drawing.Point(4, 25);
		this.IOext_tabPage.Name = "IOext_tabPage";
		this.IOext_tabPage.Padding = new System.Windows.Forms.Padding(3);
		this.IOext_tabPage.Size = new System.Drawing.Size(942, 410);
		this.IOext_tabPage.TabIndex = 5;
		this.IOext_tabPage.Text = "I/O Ext. & I2C Mux";
		this.I2Cmux_grpBox.BackColor = System.Drawing.Color.LightGreen;
		this.I2Cmux_grpBox.Controls.Add(this.SE_i2c_chkBox);
		this.I2Cmux_grpBox.Controls.Add(this.NE_i2c_chkBox);
		this.I2Cmux_grpBox.Controls.Add(this.SW_i2c_chkBox);
		this.I2Cmux_grpBox.Controls.Add(this.NW_i2c_chkBox);
		this.I2Cmux_grpBox.Controls.Add(this.pictureBox1);
		this.I2Cmux_grpBox.Controls.Add(this.Mux_I2C_CtrlReg_label);
		this.I2Cmux_grpBox.Controls.Add(this.CtrlReg_tBox);
		this.I2Cmux_grpBox.Controls.Add(this.Mux_I2C_read_but);
		this.I2Cmux_grpBox.Controls.Add(this.Mux_I2C_write_but);
		this.I2Cmux_grpBox.Controls.Add(this.I2CmuxAddr_comboBox);
		this.I2Cmux_grpBox.Controls.Add(this.Mux_I2C_addr_tBox);
		this.I2Cmux_grpBox.Location = new System.Drawing.Point(675, 9);
		this.I2Cmux_grpBox.Name = "I2Cmux_grpBox";
		this.I2Cmux_grpBox.Size = new System.Drawing.Size(194, 353);
		this.I2Cmux_grpBox.TabIndex = 23;
		this.I2Cmux_grpBox.TabStop = false;
		this.I2Cmux_grpBox.Text = "I2C Mux Settings";
		this.SE_i2c_chkBox.AutoSize = true;
		this.SE_i2c_chkBox.Location = new System.Drawing.Point(106, 119);
		this.SE_i2c_chkBox.Name = "SE_i2c_chkBox";
		this.SE_i2c_chkBox.Size = new System.Drawing.Size(57, 17);
		this.SE_i2c_chkBox.TabIndex = 22;
		this.SE_i2c_chkBox.Text = "SE i2c";
		this.SE_i2c_chkBox.UseVisualStyleBackColor = true;
		this.SE_i2c_chkBox.CheckedChanged += new System.EventHandler(Quad_I2C_Mux_CheckedChange);
		this.NE_i2c_chkBox.AutoSize = true;
		this.NE_i2c_chkBox.Location = new System.Drawing.Point(106, 77);
		this.NE_i2c_chkBox.Name = "NE_i2c_chkBox";
		this.NE_i2c_chkBox.Size = new System.Drawing.Size(58, 17);
		this.NE_i2c_chkBox.TabIndex = 21;
		this.NE_i2c_chkBox.Text = "NE i2c";
		this.NE_i2c_chkBox.UseVisualStyleBackColor = true;
		this.NE_i2c_chkBox.CheckedChanged += new System.EventHandler(Quad_I2C_Mux_CheckedChange);
		this.SW_i2c_chkBox.AutoSize = true;
		this.SW_i2c_chkBox.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.SW_i2c_chkBox.Location = new System.Drawing.Point(18, 118);
		this.SW_i2c_chkBox.Name = "SW_i2c_chkBox";
		this.SW_i2c_chkBox.Size = new System.Drawing.Size(61, 17);
		this.SW_i2c_chkBox.TabIndex = 20;
		this.SW_i2c_chkBox.Text = "SW i2c";
		this.SW_i2c_chkBox.UseVisualStyleBackColor = true;
		this.SW_i2c_chkBox.CheckedChanged += new System.EventHandler(Quad_I2C_Mux_CheckedChange);
		this.NW_i2c_chkBox.AutoSize = true;
		this.NW_i2c_chkBox.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
		this.NW_i2c_chkBox.Location = new System.Drawing.Point(18, 76);
		this.NW_i2c_chkBox.Name = "NW_i2c_chkBox";
		this.NW_i2c_chkBox.Size = new System.Drawing.Size(62, 17);
		this.NW_i2c_chkBox.TabIndex = 19;
		this.NW_i2c_chkBox.Text = "NW i2c";
		this.NW_i2c_chkBox.UseVisualStyleBackColor = true;
		this.NW_i2c_chkBox.CheckedChanged += new System.EventHandler(Quad_I2C_Mux_CheckedChange);
		this.pictureBox1.Image = (System.Drawing.Image)resources.GetObject("pictureBox1.Image");
		this.pictureBox1.Location = new System.Drawing.Point(0, 69);
		this.pictureBox1.Name = "pictureBox1";
		this.pictureBox1.Size = new System.Drawing.Size(185, 278);
		this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
		this.pictureBox1.TabIndex = 18;
		this.pictureBox1.TabStop = false;
		this.Mux_I2C_CtrlReg_label.AutoSize = true;
		this.Mux_I2C_CtrlReg_label.Location = new System.Drawing.Point(3, 46);
		this.Mux_I2C_CtrlReg_label.Name = "Mux_I2C_CtrlReg_label";
		this.Mux_I2C_CtrlReg_label.Size = new System.Drawing.Size(45, 13);
		this.Mux_I2C_CtrlReg_label.TabIndex = 17;
		this.Mux_I2C_CtrlReg_label.Text = "Ctrl Reg";
		this.CtrlReg_tBox.Location = new System.Drawing.Point(49, 43);
		this.CtrlReg_tBox.Name = "CtrlReg_tBox";
		this.CtrlReg_tBox.Size = new System.Drawing.Size(25, 20);
		this.CtrlReg_tBox.TabIndex = 16;
		this.CtrlReg_tBox.Text = "00";
		this.Mux_I2C_read_but.Location = new System.Drawing.Point(133, 42);
		this.Mux_I2C_read_but.Name = "Mux_I2C_read_but";
		this.Mux_I2C_read_but.Size = new System.Drawing.Size(52, 23);
		this.Mux_I2C_read_but.TabIndex = 15;
		this.Mux_I2C_read_but.Text = "Read";
		this.Mux_I2C_read_but.UseVisualStyleBackColor = true;
		this.Mux_I2C_read_but.Click += new System.EventHandler(Mux_I2C_read_but_Click);
		this.Mux_I2C_write_but.Location = new System.Drawing.Point(80, 42);
		this.Mux_I2C_write_but.Name = "Mux_I2C_write_but";
		this.Mux_I2C_write_but.Size = new System.Drawing.Size(52, 23);
		this.Mux_I2C_write_but.TabIndex = 14;
		this.Mux_I2C_write_but.Text = "Write";
		this.Mux_I2C_write_but.UseVisualStyleBackColor = true;
		this.Mux_I2C_write_but.Click += new System.EventHandler(Mux_I2C_write_but_Click);
		this.I2CmuxAddr_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.I2CmuxAddr_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.I2CmuxAddr_comboBox.FormattingEnabled = true;
		this.I2CmuxAddr_comboBox.Items.AddRange(new object[3] { "MUX I2C Address", "Prova I2C Address", "Custom I2C Address" });
		this.I2CmuxAddr_comboBox.Location = new System.Drawing.Point(37, 19);
		this.I2CmuxAddr_comboBox.MaxDropDownItems = 4;
		this.I2CmuxAddr_comboBox.Name = "I2CmuxAddr_comboBox";
		this.I2CmuxAddr_comboBox.Size = new System.Drawing.Size(145, 21);
		this.I2CmuxAddr_comboBox.TabIndex = 12;
		this.I2CmuxAddr_comboBox.SelectedIndexChanged += new System.EventHandler(I2CmuxAddr_comboBox_SelectedIndexChanged);
		this.Mux_I2C_addr_tBox.Location = new System.Drawing.Point(6, 19);
		this.Mux_I2C_addr_tBox.Name = "Mux_I2C_addr_tBox";
		this.Mux_I2C_addr_tBox.Size = new System.Drawing.Size(25, 20);
		this.Mux_I2C_addr_tBox.TabIndex = 11;
		this.Mux_I2C_addr_tBox.Text = "AE";
		this.groupBox6.BackColor = System.Drawing.Color.LightGreen;
		this.groupBox6.Controls.Add(this.AnaPwr_chkBox);
		this.groupBox6.Controls.Add(this.IOexpRead_but);
		this.groupBox6.Controls.Add(this.IOexpWrite_but);
		this.groupBox6.Controls.Add(this.ExtDacEn_chkBox);
		this.groupBox6.Controls.Add(this.SiLol_chkBox);
		this.groupBox6.Controls.Add(this.IO_Board_ClockSel_label);
		this.groupBox6.Controls.Add(this.SiClkInSrc_comboBox);
		this.groupBox6.Controls.Add(this.SICLKOE_chkBox);
		this.groupBox6.Controls.Add(this.IO_Board_SelDataEnSrc_label);
		this.groupBox6.Controls.Add(this.SelDataEnSrc_comboBox);
		this.groupBox6.Controls.Add(this.IO_Board_FastIN_label);
		this.groupBox6.Controls.Add(this.FastinSrc_comboBox);
		this.groupBox6.Location = new System.Drawing.Point(545, 34);
		this.groupBox6.Name = "groupBox6";
		this.groupBox6.Size = new System.Drawing.Size(124, 328);
		this.groupBox6.TabIndex = 22;
		this.groupBox6.TabStop = false;
		this.groupBox6.Text = "I/O exp BRD Settings";
		this.AnaPwr_chkBox.AutoSize = true;
		this.AnaPwr_chkBox.Location = new System.Drawing.Point(3, 228);
		this.AnaPwr_chkBox.Name = "AnaPwr_chkBox";
		this.AnaPwr_chkBox.Size = new System.Drawing.Size(92, 17);
		this.AnaPwr_chkBox.TabIndex = 14;
		this.AnaPwr_chkBox.Text = "Analog Power";
		this.AnaPwr_chkBox.UseVisualStyleBackColor = true;
		this.IOexpRead_but.Location = new System.Drawing.Point(66, 292);
		this.IOexpRead_but.Name = "IOexpRead_but";
		this.IOexpRead_but.Size = new System.Drawing.Size(52, 23);
		this.IOexpRead_but.TabIndex = 13;
		this.IOexpRead_but.Text = "Read";
		this.IOexpRead_but.UseVisualStyleBackColor = true;
		this.IOexpRead_but.Click += new System.EventHandler(IOext_gpio_Read_but_Click);
		this.IOexpWrite_but.Location = new System.Drawing.Point(6, 292);
		this.IOexpWrite_but.Name = "IOexpWrite_but";
		this.IOexpWrite_but.Size = new System.Drawing.Size(52, 23);
		this.IOexpWrite_but.TabIndex = 12;
		this.IOexpWrite_but.Text = "Write";
		this.IOexpWrite_but.UseVisualStyleBackColor = true;
		this.IOexpWrite_but.Click += new System.EventHandler(IOext_gpio_Write_but_Click);
		this.ExtDacEn_chkBox.AutoSize = true;
		this.ExtDacEn_chkBox.Location = new System.Drawing.Point(6, 61);
		this.ExtDacEn_chkBox.Name = "ExtDacEn_chkBox";
		this.ExtDacEn_chkBox.Size = new System.Drawing.Size(104, 17);
		this.ExtDacEn_chkBox.TabIndex = 11;
		this.ExtDacEn_chkBox.Text = "Disable Ext DAC";
		this.ExtDacEn_chkBox.UseVisualStyleBackColor = true;
		this.SiLol_chkBox.AutoSize = true;
		this.SiLol_chkBox.Enabled = false;
		this.SiLol_chkBox.Location = new System.Drawing.Point(3, 189);
		this.SiLol_chkBox.Name = "SiLol_chkBox";
		this.SiLol_chkBox.Size = new System.Drawing.Size(89, 17);
		this.SiLol_chkBox.TabIndex = 10;
		this.SiLol_chkBox.Text = "Loss Of Lock";
		this.SiLol_chkBox.UseVisualStyleBackColor = true;
		this.IO_Board_ClockSel_label.AutoSize = true;
		this.IO_Board_ClockSel_label.Location = new System.Drawing.Point(7, 147);
		this.IO_Board_ClockSel_label.Name = "IO_Board_ClockSel_label";
		this.IO_Board_ClockSel_label.Size = new System.Drawing.Size(70, 13);
		this.IO_Board_ClockSel_label.TabIndex = 9;
		this.IO_Board_ClockSel_label.Text = "SI CLK IN sel";
		this.SiClkInSrc_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.SiClkInSrc_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.SiClkInSrc_comboBox.FormattingEnabled = true;
		this.SiClkInSrc_comboBox.Items.AddRange(new object[4] { "SMA", "KRIA", "NONE", "Crystal" });
		this.SiClkInSrc_comboBox.Location = new System.Drawing.Point(4, 162);
		this.SiClkInSrc_comboBox.Name = "SiClkInSrc_comboBox";
		this.SiClkInSrc_comboBox.Size = new System.Drawing.Size(85, 21);
		this.SiClkInSrc_comboBox.TabIndex = 8;
		this.SICLKOE_chkBox.AutoSize = true;
		this.SICLKOE_chkBox.Location = new System.Drawing.Point(4, 129);
		this.SICLKOE_chkBox.Name = "SICLKOE_chkBox";
		this.SICLKOE_chkBox.Size = new System.Drawing.Size(83, 17);
		this.SICLKOE_chkBox.TabIndex = 7;
		this.SICLKOE_chkBox.Text = "SI_CLK_OE";
		this.SICLKOE_chkBox.UseVisualStyleBackColor = true;
		this.IO_Board_SelDataEnSrc_label.AutoSize = true;
		this.IO_Board_SelDataEnSrc_label.Location = new System.Drawing.Point(9, 84);
		this.IO_Board_SelDataEnSrc_label.Name = "IO_Board_SelDataEnSrc_label";
		this.IO_Board_SelDataEnSrc_label.Size = new System.Drawing.Size(83, 13);
		this.IO_Board_SelDataEnSrc_label.TabIndex = 3;
		this.IO_Board_SelDataEnSrc_label.Text = "Sel Data En Src";
		this.SelDataEnSrc_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.SelDataEnSrc_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.SelDataEnSrc_comboBox.FormattingEnabled = true;
		this.SelDataEnSrc_comboBox.Items.AddRange(new object[2] { "KRIA", "Serv. Con. J14" });
		this.SelDataEnSrc_comboBox.Location = new System.Drawing.Point(6, 99);
		this.SelDataEnSrc_comboBox.Name = "SelDataEnSrc_comboBox";
		this.SelDataEnSrc_comboBox.Size = new System.Drawing.Size(85, 21);
		this.SelDataEnSrc_comboBox.TabIndex = 2;
		this.IO_Board_FastIN_label.AutoSize = true;
		this.IO_Board_FastIN_label.Location = new System.Drawing.Point(9, 19);
		this.IO_Board_FastIN_label.Name = "IO_Board_FastIN_label";
		this.IO_Board_FastIN_label.Size = new System.Drawing.Size(80, 13);
		this.IO_Board_FastIN_label.TabIndex = 1;
		this.IO_Board_FastIN_label.Text = "FASTIN source";
		this.FastinSrc_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.FastinSrc_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.FastinSrc_comboBox.FormattingEnabled = true;
		this.FastinSrc_comboBox.Items.AddRange(new object[2] { "S.Ended J12/J11", "LVDS" });
		this.FastinSrc_comboBox.Location = new System.Drawing.Point(6, 34);
		this.FastinSrc_comboBox.Name = "FastinSrc_comboBox";
		this.FastinSrc_comboBox.Size = new System.Drawing.Size(85, 21);
		this.FastinSrc_comboBox.TabIndex = 0;
		this.IOextI2C_read_all_but.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.IOextI2C_read_all_but.Location = new System.Drawing.Point(464, 6);
		this.IOextI2C_read_all_but.Name = "IOextI2C_read_all_but";
		this.IOextI2C_read_all_but.Size = new System.Drawing.Size(75, 25);
		this.IOextI2C_read_all_but.TabIndex = 16;
		this.IOextI2C_read_all_but.Text = "Read All";
		this.IOextI2C_read_all_but.UseVisualStyleBackColor = true;
		this.IOextI2C_read_all_but.Click += new System.EventHandler(IOextI2C_read_all_but_Click);
		this.IOextI2C_write_all_but.ContextMenuStrip = this.TDCwriteAll_contMenuStrip;
		this.IOextI2C_write_all_but.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.IOextI2C_write_all_but.Location = new System.Drawing.Point(383, 6);
		this.IOextI2C_write_all_but.Name = "IOextI2C_write_all_but";
		this.IOextI2C_write_all_but.Size = new System.Drawing.Size(75, 25);
		this.IOextI2C_write_all_but.TabIndex = 15;
		this.IOextI2C_write_all_but.Text = "Write All";
		this.IOextI2C_write_all_but.UseVisualStyleBackColor = true;
		this.IOextI2C_write_all_but.Click += new System.EventHandler(IOextI2C_write_all_but_Click);
		this.IOextI2C_read_single_but.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.IOextI2C_read_single_but.Location = new System.Drawing.Point(282, 6);
		this.IOextI2C_read_single_but.Name = "IOextI2C_read_single_but";
		this.IOextI2C_read_single_but.Size = new System.Drawing.Size(75, 25);
		this.IOextI2C_read_single_but.TabIndex = 14;
		this.IOextI2C_read_single_but.Text = "Read Single";
		this.IOextI2C_read_single_but.UseVisualStyleBackColor = true;
		this.IOextI2C_read_single_but.Click += new System.EventHandler(IOextI2C_read_single_but_Click);
		this.IOextI2C_write_single_but.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.IOextI2C_write_single_but.Location = new System.Drawing.Point(201, 6);
		this.IOextI2C_write_single_but.Name = "IOextI2C_write_single_but";
		this.IOextI2C_write_single_but.Size = new System.Drawing.Size(75, 25);
		this.IOextI2C_write_single_but.TabIndex = 13;
		this.IOextI2C_write_single_but.Text = "Write Single";
		this.IOextI2C_write_single_but.UseVisualStyleBackColor = true;
		this.IOextI2C_write_single_but.Click += new System.EventHandler(IOextI2C_write_single_but_Click);
		this.IOext_dGridView.AllowUserToAddRows = false;
		dataGridViewCellStyle5.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
		dataGridViewCellStyle5.BackColor = System.Drawing.SystemColors.Control;
		dataGridViewCellStyle5.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		dataGridViewCellStyle5.ForeColor = System.Drawing.SystemColors.WindowText;
		dataGridViewCellStyle5.SelectionBackColor = System.Drawing.SystemColors.Highlight;
		dataGridViewCellStyle5.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
		dataGridViewCellStyle5.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
		this.IOext_dGridView.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle5;
		this.IOext_dGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
		this.IOext_dGridView.Columns.AddRange(this.dataGridViewTextBoxColumn1, this.dataGridViewTextBoxColumn2, this.dataGridViewTextBoxColumn3, this.dataGridViewTextBoxColumn4, this.dataGridViewTextBoxColumn5, this.dataGridViewTextBoxColumn6, this.dataGridViewTextBoxColumn7, this.dataGridViewTextBoxColumn8, this.dataGridViewTextBoxColumn9, this.dataGridViewTextBoxColumn10, this.dataGridViewTextBoxColumn11, this.dataGridViewTextBoxColumn12, this.dataGridViewTextBoxColumn13, this.dataGridViewTextBoxColumn14, this.dataGridViewTextBoxColumn15, this.dataGridViewTextBoxColumn16);
		dataGridViewCellStyle6.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
		dataGridViewCellStyle6.BackColor = System.Drawing.SystemColors.Window;
		dataGridViewCellStyle6.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		dataGridViewCellStyle6.ForeColor = System.Drawing.SystemColors.ControlText;
		dataGridViewCellStyle6.SelectionBackColor = System.Drawing.SystemColors.Highlight;
		dataGridViewCellStyle6.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
		dataGridViewCellStyle6.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
		this.IOext_dGridView.DefaultCellStyle = dataGridViewCellStyle6;
		this.IOext_dGridView.Location = new System.Drawing.Point(6, 34);
		this.IOext_dGridView.Name = "IOext_dGridView";
		this.IOext_dGridView.RowHeadersWidth = 40;
		this.IOext_dGridView.Size = new System.Drawing.Size(533, 328);
		this.IOext_dGridView.TabIndex = 12;
		this.dataGridViewTextBoxColumn1.HeaderText = "00";
		this.dataGridViewTextBoxColumn1.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn1.Name = "dataGridViewTextBoxColumn1";
		this.dataGridViewTextBoxColumn1.Width = 30;
		this.dataGridViewTextBoxColumn2.HeaderText = "01";
		this.dataGridViewTextBoxColumn2.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn2.Name = "dataGridViewTextBoxColumn2";
		this.dataGridViewTextBoxColumn2.Width = 30;
		this.dataGridViewTextBoxColumn3.HeaderText = "02";
		this.dataGridViewTextBoxColumn3.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn3.Name = "dataGridViewTextBoxColumn3";
		this.dataGridViewTextBoxColumn3.Width = 30;
		this.dataGridViewTextBoxColumn4.HeaderText = "03";
		this.dataGridViewTextBoxColumn4.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn4.Name = "dataGridViewTextBoxColumn4";
		this.dataGridViewTextBoxColumn4.Width = 30;
		this.dataGridViewTextBoxColumn5.HeaderText = "04";
		this.dataGridViewTextBoxColumn5.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn5.Name = "dataGridViewTextBoxColumn5";
		this.dataGridViewTextBoxColumn5.Width = 30;
		this.dataGridViewTextBoxColumn6.HeaderText = "05";
		this.dataGridViewTextBoxColumn6.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn6.Name = "dataGridViewTextBoxColumn6";
		this.dataGridViewTextBoxColumn6.Width = 30;
		this.dataGridViewTextBoxColumn7.HeaderText = "06";
		this.dataGridViewTextBoxColumn7.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn7.Name = "dataGridViewTextBoxColumn7";
		this.dataGridViewTextBoxColumn7.Width = 30;
		this.dataGridViewTextBoxColumn8.HeaderText = "07";
		this.dataGridViewTextBoxColumn8.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn8.Name = "dataGridViewTextBoxColumn8";
		this.dataGridViewTextBoxColumn8.Width = 30;
		this.dataGridViewTextBoxColumn9.HeaderText = "08";
		this.dataGridViewTextBoxColumn9.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn9.Name = "dataGridViewTextBoxColumn9";
		this.dataGridViewTextBoxColumn9.Width = 30;
		this.dataGridViewTextBoxColumn10.HeaderText = "09";
		this.dataGridViewTextBoxColumn10.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn10.Name = "dataGridViewTextBoxColumn10";
		this.dataGridViewTextBoxColumn10.Width = 30;
		this.dataGridViewTextBoxColumn11.HeaderText = "0A";
		this.dataGridViewTextBoxColumn11.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn11.Name = "dataGridViewTextBoxColumn11";
		this.dataGridViewTextBoxColumn11.Width = 30;
		this.dataGridViewTextBoxColumn12.HeaderText = "0B";
		this.dataGridViewTextBoxColumn12.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn12.Name = "dataGridViewTextBoxColumn12";
		this.dataGridViewTextBoxColumn12.Width = 30;
		this.dataGridViewTextBoxColumn13.HeaderText = "0C";
		this.dataGridViewTextBoxColumn13.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn13.Name = "dataGridViewTextBoxColumn13";
		this.dataGridViewTextBoxColumn13.Width = 30;
		this.dataGridViewTextBoxColumn14.HeaderText = "0D";
		this.dataGridViewTextBoxColumn14.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn14.Name = "dataGridViewTextBoxColumn14";
		this.dataGridViewTextBoxColumn14.Width = 30;
		this.dataGridViewTextBoxColumn15.HeaderText = "0E";
		this.dataGridViewTextBoxColumn15.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn15.Name = "dataGridViewTextBoxColumn15";
		this.dataGridViewTextBoxColumn15.Width = 30;
		this.dataGridViewTextBoxColumn16.HeaderText = "0F";
		this.dataGridViewTextBoxColumn16.MinimumWidth = 6;
		this.dataGridViewTextBoxColumn16.Name = "dataGridViewTextBoxColumn16";
		this.dataGridViewTextBoxColumn16.Width = 30;
		this.IOextAddr_comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.IOextAddr_comboBox.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
		this.IOextAddr_comboBox.FormattingEnabled = true;
		this.IOextAddr_comboBox.Items.AddRange(new object[3] { "I/O ext I2C Address", "Prova I2C Address", "Custom I2C Address" });
		this.IOextAddr_comboBox.Location = new System.Drawing.Point(40, 9);
		this.IOextAddr_comboBox.MaxDropDownItems = 4;
		this.IOextAddr_comboBox.Name = "IOextAddr_comboBox";
		this.IOextAddr_comboBox.Size = new System.Drawing.Size(145, 21);
		this.IOextAddr_comboBox.TabIndex = 11;
		this.IOextAddr_comboBox.SelectedIndexChanged += new System.EventHandler(IOextAddr_comboBox_SelectedIndexChanged);
		this.IOext_I2C_addr_tBox.Location = new System.Drawing.Point(9, 8);
		this.IOext_I2C_addr_tBox.Name = "IOext_I2C_addr_tBox";
		this.IOext_I2C_addr_tBox.Size = new System.Drawing.Size(25, 20);
		this.IOext_I2C_addr_tBox.TabIndex = 10;
		this.IOext_I2C_addr_tBox.Text = "AE";
		this.FIXED_PATTERN_textBox.BackColor = System.Drawing.Color.White;
		this.FIXED_PATTERN_textBox.Location = new System.Drawing.Point(221, 53);
		this.FIXED_PATTERN_textBox.Name = "FIXED_PATTERN_textBox";
		this.FIXED_PATTERN_textBox.ReadOnly = true;
		this.FIXED_PATTERN_textBox.Size = new System.Drawing.Size(25, 20);
		this.FIXED_PATTERN_textBox.TabIndex = 34;
		this.FIXED_PATTERN_textBox.Text = "5A";
		this.FIXED_PATTERN_textBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
		base.AutoScaleDimensions = new System.Drawing.SizeF(6f, 13f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		this.BackColor = System.Drawing.SystemColors.Control;
		base.ClientSize = new System.Drawing.Size(978, 607);
		base.Controls.Add(this.Log_textBox);
		base.Controls.Add(this.statusStrip1);
		base.Controls.Add(this.tabControl1);
		base.Controls.Add(this.menuStrip1);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
		base.MainMenuStrip = this.menuStrip1;
		base.Name = "MainForm";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "TB_IGNITE64";
		base.FormClosing += new System.Windows.Forms.FormClosingEventHandler(MainForm_FormClosing);
		base.Load += new System.EventHandler(MainForm_Load);
		this.TDCcalibAll_contMenuStrip.ResumeLayout(false);
		this.TDCwriteAll_contMenuStrip.ResumeLayout(false);
		this.menuStrip1.ResumeLayout(false);
		this.menuStrip1.PerformLayout();
		this.statusStrip1.ResumeLayout(false);
		this.statusStrip1.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.SEL_CAL_TIME_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.PIX_Sel_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DCO0ctrl_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DCO0adj_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DCO_PIX_adj_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DCO_PIX_ctrl_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_FT_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_VTH_H_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_FT_SEL_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_VTH_L_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_VINJ_H_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_VINJ_L_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_VLDO_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.MAT_DAC_VFB_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DAC_IDISC_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DAC_ICSA_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DAC_IKRUM_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.CH_SEL_42_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.CH_SEL_41_UpDown).EndInit();
		this.exAdcDac_tabPage.ResumeLayout(false);
		this.exAdcDac_tabPage.PerformLayout();
		this.Quad_DAC_grpBox.ResumeLayout(false);
		this.Quad_DAC_grpBox.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.DACicap_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DACiref_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DACvfeed_UpDown).EndInit();
		this.groupBox1.ResumeLayout(false);
		this.groupBox2.ResumeLayout(false);
		this.groupBox2.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.ScanVthStep_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.ScanVthMin_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.ScanVthMax_UpDown).EndInit();
		this.groupBox4.ResumeLayout(false);
		this.groupBox4.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.ScanVinj2Step_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.ScanVinj2Min_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.ScanVinj2Max_UpDown).EndInit();
		this.Quad_ADC_grpBox.ResumeLayout(false);
		this.Quad_ADC_grpBox.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.ADCdac_Quad_NShot_numUpDown).EndInit();
		this.CurMonADC_groupBox.ResumeLayout(false);
		this.CurMonADC_groupBox.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.ADCcommon_NShot_numUpDown).EndInit();
		this.ExtDAC_groupBox.ResumeLayout(false);
		this.ExtDAC_groupBox.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.DACvref_L_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DACvthr_H_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DACvthr_L_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DACvinj_H_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.DACVext_UpDown).EndInit();
		this.Ctrl_tabPage.ResumeLayout(false);
		this.groupBox3.ResumeLayout(false);
		this.Mat_tabPage.ResumeLayout(false);
		this.Mat_tabPage.PerformLayout();
		this.MAT_Test_Routines_groupBox.ResumeLayout(false);
		this.MAT_AFE_groupBox.ResumeLayout(false);
		this.MAT_AFE_groupBox.PerformLayout();
		this.PIX_groupBox.ResumeLayout(false);
		this.PIX_groupBox.PerformLayout();
		this.MAT_DAC_groupBox.ResumeLayout(false);
		this.MAT_DAC_groupBox.PerformLayout();
		this.MAT_cfg_groupBox.ResumeLayout(false);
		this.MAT_cfg_groupBox.PerformLayout();
		this.MAT_COMMANDS_groupBox.ResumeLayout(false);
		this.MAT_COMMANDS_groupBox.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.Mat_dGridView).EndInit();
		this.Top_tabPage.ResumeLayout(false);
		this.Top_tabPage.PerformLayout();
		this.TOP_COMMANDS_groupBox.ResumeLayout(false);
		this.TOP_COMMANDS_groupBox.PerformLayout();
		this.TOP_TDCpulse_contextMenuStrip.ResumeLayout(false);
		this.CAP_MEAS_groupBox.ResumeLayout(false);
		this.CAP_MEAS_groupBox.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.CMES_CC_04F_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.CMES_CQ_20F_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.CMES_CF_20F_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.CMES_SEL_WAIT_UpDown).EndInit();
		this.AFE_PULSE_groupBox.ResumeLayout(false);
		this.AFE_PULSE_groupBox.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.AFE_TP_REPETITION_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.AFE_TP_WIDTH_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.AFE_TP_PERIOD_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.AFE_EN_TP_PHASE_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.AFE_LISTEN_TIME_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.AFE_UPDATE_TIME_UpDown).EndInit();
		this.GPO_OUT_SEL_groupBox.ResumeLayout(false);
		this.GPO_OUT_SEL_groupBox.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.FIXED_PATTERN_UpDown).EndInit();
		this.TDC_PULSE_groupBox.ResumeLayout(false);
		this.TDC_PULSE_groupBox.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.POINT_TOT_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.POINT_TA_UpDown).EndInit();
		this.BXID_groupBox.ResumeLayout(false);
		this.BXID_groupBox.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.BXID_LMT_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.BXID_PRL_UpDown).EndInit();
		this.IOSetSel_groupBox.ResumeLayout(false);
		this.IOSetSel_groupBox.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.SLVS_DRV_STR_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.SLVS_CMM_MODE_UpDown).EndInit();
		((System.ComponentModel.ISupportInitialize)this.Top_dGridView).EndInit();
		this.DEF_CONFIG_groupBox.ResumeLayout(false);
		this.DEF_CONFIG_groupBox.PerformLayout();
		this.tabControl1.ResumeLayout(false);
		this.IOext_tabPage.ResumeLayout(false);
		this.IOext_tabPage.PerformLayout();
		this.I2Cmux_grpBox.ResumeLayout(false);
		this.I2Cmux_grpBox.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox1).EndInit();
		this.groupBox6.ResumeLayout(false);
		this.groupBox6.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.IOext_dGridView).EndInit();
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
