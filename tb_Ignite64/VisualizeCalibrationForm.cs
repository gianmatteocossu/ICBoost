using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace tb_Ignite64;

public class VisualizeCalibrationForm : Form
{
	public class BufferedPanel : Panel
	{
		public new event MouseEventHandler MouseWheel
		{
			add
			{
				base.MouseWheel += value;
			}
			remove
			{
				base.MouseWheel -= value;
			}
		}

		public BufferedPanel()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			UpdateStyles();
		}

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == 522)
			{
				int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
				Point point = PointToClient(Control.MousePosition);
				OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, delta));
			}
			else
			{
				base.WndProc(ref m);
			}
		}
	}

	private MainForm MyMain;

	private double[,,,] matrix;

	public double threshold = 3.0;

	public double required_res = 30.0;

	public double max_tolerance = 50.0;

	public double max_value;

	private Panel drawingPanel;

	private Bitmap cachedBitmap;

	private Timer refreshTimer;

	private Brush textBrush = Brushes.Black;

	private Brush textBrushSq = Brushes.Black;

	private Point _panStart;

	private Point _offset = Point.Empty;

	private bool _panning;

	private float _scale = 1f;

	private const float MinScale = 0.2f;

	private const float MaxScale = 5f;

	private IContainer components;

	private Button Test_but;

	private CheckBox Test_chkBox;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ExStyle |= 33554432;
			return createParams;
		}
	}

	public VisualizeCalibrationForm(MainForm TopForm)
	{
		MyMain = TopForm;
		matrix = MainForm.LoadedData.CAL_Matrix;
		Text = "Calibration Matrix Visualization";
		base.Size = new Size(1024, 1024);
		DoubleBuffered = true;
		drawingPanel = new BufferedPanel
		{
			AutoScroll = true,
			Size = new Size(1600, 1600),
			Location = new Point(0, 40),
			TabStop = true
		};
		base.Controls.Add(drawingPanel);
		drawingPanel.Paint += DrawMatrix;
		drawingPanel.MouseDown += DrawingPanel_MouseDown;
		drawingPanel.MouseMove += DrawingPanel_MouseMove;
		drawingPanel.MouseUp += DrawingPanel_MouseUp;
		drawingPanel.MouseLeave += DrawingPanel_MouseLeave;
		drawingPanel.MouseWheel += DrawingPanel_MouseWheel;
		drawingPanel.Focus();
		refreshTimer = new Timer();
		refreshTimer.Interval = 500;
		refreshTimer.Tick += RefreshTimer_Tick;
		refreshTimer.Start();
		InitializeComponent();
	}

	private void DrawMatrix(object sender, PaintEventArgs e)
	{
		if (cachedBitmap == null)
		{
			cachedBitmap = new Bitmap(drawingPanel.Width, drawingPanel.Height);
			using Graphics g = Graphics.FromImage(cachedBitmap);
			DrawToBitmap(g);
		}
		Matrix matrix = new Matrix();
		matrix.Translate(_offset.X, _offset.Y);
		matrix.Scale(_scale, _scale);
		e.Graphics.Transform = matrix;
		e.Graphics.DrawImage(cachedBitmap, 0, 0);
		e.Graphics.ResetTransform();
	}

	private void DrawToBitmap(Graphics g)
	{
		int num = 32;
		int num2 = 16;
		for (int i = 0; i < 16; i++)
		{
			int num3 = i % 4 * (8 * num + num2) + 20;
			int num4 = i / 4 * (8 * num + num2 + 20) + 40;
			string s = $"Mattonella {i}";
			Font font = new Font(FontFamily.GenericSansSerif, 14f, FontStyle.Regular, GraphicsUnit.Point);
			g.DrawString(s, font, textBrush, num3 + num2, num4 - 22);
			for (int j = 0; j < 64; j++)
			{
				double num5 = matrix[MainForm.Cur_Quad, i, j, 0];
				double num6 = matrix[MainForm.Cur_Quad, i, j, 1];
				double num7 = Math.Abs(num5 - num6);
				if (num7 > max_value && num7 < 100.0)
				{
					max_value = num7;
				}
				Color squareColor = GetSquareColor(num7, num5, num6);
				Brush brush = new SolidBrush(squareColor);
				g.FillRectangle(brush, num3 + (num + 1) * (j % 8), num4 + (num + 1) * (j / 8), num, num);
				string s2 = $"{Math.Round(num5 - num6, 1)}";
				float num8 = 0.05f;
				float num9 = 0.3f;
				float num10 = (float)num3 + (float)(num + 1) * ((float)(j % 8) + num8);
				float num11 = (float)num4 + (float)(num + 1) * ((float)(j / 8) + num9);
				g.DrawString(s2, Font, textBrushSq, num10, num11);
			}
		}
	}

	private Color GetSquareColor(double diff, double DCO0 = 90.0, double DCO1 = 60.0)
	{
		Color lightGray = Color.LightGray;
		Color lightGreen = Color.LightGreen;
		Color yellow = Color.Yellow;
		Color orange = Color.Orange;
		Color red = Color.Red;
		double num = Math.Max(0.0, Math.Min(Math.Abs(diff - required_res) / (2.0 * threshold), 1.0));
		if (DCO0 < 10.0 || DCO1 < 10.0)
		{
			return lightGray;
		}
		if (diff > max_tolerance)
		{
			num = Math.Max(0.0, Math.Min((diff - max_tolerance) / (max_value - max_tolerance), 1.0));
			return InterpolateColor(orange, red, num);
		}
		if (num == 1.0)
		{
			num = Math.Max(0.0, Math.Min(diff / max_tolerance, 1.0));
			return InterpolateColor(yellow, orange, num);
		}
		return InterpolateColor(lightGreen, yellow, num);
	}

	private Color InterpolateColor(Color start, Color end, double ratio)
	{
		int red = (int)((double)(int)start.R + (double)(end.R - start.R) * ratio);
		int green = (int)((double)(int)start.G + (double)(end.G - start.G) * ratio);
		int blue = (int)((double)(int)start.B + (double)(end.B - start.B) * ratio);
		return Color.FromArgb(red, green, blue);
	}

	private void DrawingPanel_MouseDown(object sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			_panning = true;
			_panStart = e.Location;
			drawingPanel.Cursor = Cursors.Hand;
		}
	}

	private void DrawingPanel_MouseMove(object sender, MouseEventArgs e)
	{
		if (_panning)
		{
			int num = e.X - _panStart.X;
			int num2 = e.Y - _panStart.Y;
			_offset.X += num;
			_offset.Y += num2;
			_panStart = e.Location;
			drawingPanel.Invalidate();
		}
	}

	private void DrawingPanel_MouseUp(object sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			_panning = false;
			drawingPanel.Cursor = Cursors.Default;
		}
	}

	private void DrawingPanel_MouseLeave(object sender, EventArgs e)
	{
		_panning = false;
		drawingPanel.Cursor = Cursors.Default;
	}

	private void DrawingPanel_MouseWheel(object sender, MouseEventArgs e)
	{
		float num = ((e.Delta > 0) ? 1.15f : 0.86956525f);
		float num2 = _scale * num;
		if (num2 < 0.2f)
		{
			num2 = 0.2f;
		}
		if (num2 > 5f)
		{
			num2 = 5f;
		}
		PointF pointF = ScreenToWorld(e.Location);
		_scale = num2;
		PointF pointF2 = ScreenToWorld(e.Location);
		_offset.X += (int)((pointF2.X - pointF.X) * _scale);
		_offset.Y += (int)((pointF2.Y - pointF.Y) * _scale);
		drawingPanel.Invalidate();
	}

	private PointF ScreenToWorld(Point p)
	{
		return new PointF((float)(p.X - _offset.X) / _scale, (float)(p.Y - _offset.Y) / _scale);
	}

	private void RefreshTimer_Tick(object sender, EventArgs e)
	{
		cachedBitmap?.Dispose();
		cachedBitmap = null;
		drawingPanel.Invalidate();
	}

	private void ClampOffset()
	{
		int num = (int)((float)cachedBitmap.Width * _scale);
		int num2 = (int)((float)cachedBitmap.Height * _scale);
		int num3 = drawingPanel.ClientSize.Width;
		int num4 = drawingPanel.ClientSize.Height;
		int num5 = Math.Max(0, num3 - num);
		int num6 = Math.Max(0, num4 - num2);
		if (_offset.X > 0)
		{
			_offset.X = 0;
		}
		if (_offset.Y > 0)
		{
			_offset.Y = 0;
		}
		if (_offset.X < num5)
		{
			_offset.X = num5;
		}
		if (_offset.Y < num6)
		{
			_offset.Y = num6;
		}
	}

	private void Test_chkBox_CheckedChanged(object sender, EventArgs e)
	{
		if (Test_chkBox.Checked)
		{
			BackColor = Color.Black;
			Test_chkBox.ForeColor = Color.White;
			textBrush = Brushes.White;
		}
		else
		{
			BackColor = Color.White;
			Test_chkBox.ForeColor = Color.Black;
			textBrush = Brushes.Black;
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
		this.Test_but = new System.Windows.Forms.Button();
		this.Test_chkBox = new System.Windows.Forms.CheckBox();
		base.SuspendLayout();
		this.Test_but.BackColor = System.Drawing.Color.Gold;
		this.Test_but.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.Test_but.Location = new System.Drawing.Point(10, 10);
		this.Test_but.Name = "Test_but";
		this.Test_but.Size = new System.Drawing.Size(75, 25);
		this.Test_but.TabIndex = 0;
		this.Test_but.Text = "Test But";
		this.Test_but.UseVisualStyleBackColor = false;
		this.Test_chkBox.AutoSize = true;
		this.Test_chkBox.BackColor = System.Drawing.Color.Transparent;
		this.Test_chkBox.ForeColor = System.Drawing.SystemColors.ControlText;
		this.Test_chkBox.Location = new System.Drawing.Point(650, 10);
		this.Test_chkBox.Name = "Test_chkBox";
		this.Test_chkBox.Size = new System.Drawing.Size(79, 17);
		this.Test_chkBox.TabIndex = 1;
		this.Test_chkBox.Text = "Dark Mode";
		this.Test_chkBox.UseVisualStyleBackColor = false;
		this.Test_chkBox.CheckedChanged += new System.EventHandler(Test_chkBox_CheckedChanged);
		base.AutoScaleDimensions = new System.Drawing.SizeF(96f, 96f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
		this.AutoScroll = true;
		this.BackColor = System.Drawing.Color.White;
		base.ClientSize = new System.Drawing.Size(749, 559);
		base.Controls.Add(this.Test_chkBox);
		base.Controls.Add(this.Test_but);
		base.Name = "VisualizeCalibrationForm";
		base.ShowIcon = false;
		this.Text = "LSB Map (Least Significant Bin)";
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
