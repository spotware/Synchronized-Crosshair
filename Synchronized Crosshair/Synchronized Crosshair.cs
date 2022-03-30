using cAlgo.API;
using System.Collections.Concurrent;
using System;
using System.Collections.Generic;

namespace cAlgo
{
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class SynchronizedCrosshair : Indicator
    {
        private static ConcurrentDictionary<string, IndicatorInstanceContainer> _indicatorInstances = new ConcurrentDictionary<string, IndicatorInstanceContainer>();

        private string _chartKey;

        private string _horizontalLineObjectName;

        private string _verticalLineObjectName;

        private string _lineObjectName;

        private ChartHorizontalLine _horizontalLine;

        private ChartVerticalLine _verticalLine;

        private ChartTrendLine _line;

        private DateTime _lastMouseLocationTime;

        private double _lastMouseLocationPrice;

        private bool _isActive;

        private DateTime _lastMoveTime;

        private DataBoxControl _dataBoxControl;

        [Parameter("Mode", DefaultValue = Mode.All, Group = "General")]
        public Mode Mode { get; set; }

        [Parameter("Horizontal Alignment", DefaultValue = HorizontalAlignment.Right, Group = "Data Box")]
        public HorizontalAlignment DataBoxHorizontalAlignment { get; set; }

        [Parameter("Vertical Alignment", DefaultValue = VerticalAlignment.Bottom, Group = "Data Box")]
        public VerticalAlignment DataBoxVerticalAlignment { get; set; }

        [Parameter("Opacity", DefaultValue = 0.8, MinValue = 0, MaxValue = 1, Group = "Data Box")]
        public double DataBoxOpacity { get; set; }

        [Parameter("Margin", DefaultValue = 1, MinValue = 0, Group = "Data Box")]
        public double DataBoxMargin { get; set; }

        protected override void Initialize()
        {
            _chartKey = string.Format("{0}_{1}_{2}", SymbolName, TimeFrame, Chart.ChartType);
            _horizontalLineObjectName = string.Format("{0}_Horizontal", _chartKey);
            _verticalLineObjectName = string.Format("{0}_Vertical", _chartKey);
            _lineObjectName = string.Format("{0}_Line", _chartKey);

            _indicatorInstances.AddOrUpdate(_chartKey, new IndicatorInstanceContainer(this), (key, value) => new IndicatorInstanceContainer(this));

            _dataBoxControl = new DataBoxControl
            {
                HorizontalAlignment = DataBoxHorizontalAlignment,
                VerticalAlignment = DataBoxVerticalAlignment,
                Opacity = DataBoxOpacity,
                IsVisible = false,
                Margin = DataBoxMargin
            };

            Chart.AddControl(_dataBoxControl);

            Chart.MouseMove += Chart_MouseMove;
            Chart.MouseDown += Chart_MouseDown;
        }

        public override void Calculate(int index)
        {
        }

        public void OnMouseDown()
        {
            _isActive = false;

            if (_horizontalLine != null)
            {
                Chart.RemoveObject(_horizontalLineObjectName);

                _horizontalLine = null;
            }

            if (_verticalLine != null)
            {
                Chart.RemoveObject(_verticalLineObjectName);

                _verticalLine = null;
            }

            if (_line != null)
            {
                Chart.RemoveObject(_lineObjectName);

                _line = null;
            }

            _dataBoxControl.IsVisible = false;
        }

        public void ShowCrosshair(DateTime timeValue, double yValue, bool ctrlKey)
        {
            if (_isActive && ctrlKey == false)
            {
                if (_line == null)
                {
                    _line = Chart.DrawTrendLine(_lineObjectName, _lastMouseLocationTime, _lastMouseLocationPrice, timeValue, yValue, Chart.ColorSettings.ForegroundColor);
                }
                else
                {
                    _line.Time2 = timeValue;
                    _line.Y2 = yValue;
                }

                var timeValueOffset = new DateTimeOffset(timeValue, TimeSpan.FromSeconds(0));

                _dataBoxControl.Time = timeValueOffset.ToOffset(Application.UserTimeOffset).ToString("dd/MM/yyyy HH:mm");
                _dataBoxControl.Pips = Math.Round(GetInPips(Math.Abs(_line.Y2 - _line.Y1)), 2).ToString();
                _dataBoxControl.Periods = Math.Abs(Bars.OpenTimes.GetIndexByTime(_line.Time1) - Bars.OpenTimes.GetIndexByTime(_line.Time2)).ToString();
                _dataBoxControl.Price = Math.Round(yValue, Symbol.Digits).ToString();
                _dataBoxControl.IsVisible = true;
            }
            else if (ctrlKey)
            {
                _isActive = true;

                if (_horizontalLine == null)
                {
                    _horizontalLine = Chart.DrawHorizontalLine(_horizontalLineObjectName, yValue, Chart.ColorSettings.ForegroundColor);
                }
                else
                {
                    _horizontalLine.Y = yValue;
                }

                if (_verticalLine == null)
                {
                    _verticalLine = Chart.DrawVerticalLine(_verticalLineObjectName, timeValue, Chart.ColorSettings.ForegroundColor);
                }
                else
                {
                    _verticalLine.Time = timeValue;
                }
            }

            _lastMouseLocationTime = timeValue;
            _lastMouseLocationPrice = yValue;
        }

        private double GetInPips(double price)
        {
            return price * (Symbol.TickSize / Symbol.PipSize * Math.Pow(10, Symbol.Digits));
        }

        private void Chart_MouseDown(ChartMouseEventArgs obj)
        {
            if (_isActive == false) return;

            OnMouseDown();

            TriggerOnMouseDownOnCharts();
        }

        private void Chart_MouseMove(ChartMouseEventArgs obj)
        {
            if (Server.TimeInUtc - _lastMoveTime < TimeSpan.FromMilliseconds(1)) return;

            _lastMoveTime = Server.TimeInUtc;

            ShowCrosshair(obj.TimeValue, obj.YValue, obj.CtrlKey);

            ShowCrosshairOnCharts(obj);
        }

        private List<KeyValuePair<string, SynchronizedCrosshair>> GetIndicators()
        {
            Func<SynchronizedCrosshair, bool> predicate;

            switch (Mode)
            {
                case Mode.Symbol:
                    predicate = indicator => indicator.SymbolName.Equals(SymbolName, StringComparison.Ordinal);
                    break;

                case Mode.TimeFrame:
                    predicate = indicator => indicator.TimeFrame == TimeFrame;
                    break;

                default:
                    predicate = null;
                    break;
            }

            var result = new List<KeyValuePair<string, SynchronizedCrosshair>>(_indicatorInstances.Values.Count);

            foreach (var indicatorContianer in _indicatorInstances)
            {
                SynchronizedCrosshair indicator;

                if (indicatorContianer.Value.GetIndicator(out indicator) == false || indicator == this || (predicate != null && predicate(indicator) == false)) continue;

                result.Add(new KeyValuePair<string, SynchronizedCrosshair>(indicatorContianer.Key, indicator));
            }

            return result;
        }

        private void ShowCrosshairOnCharts(ChartMouseEventArgs mouseEventArgs)
        {
            var indicators = GetIndicators();

            var topToBottomDiff = Chart.TopY - Chart.BottomY;
            var diff = mouseEventArgs.YValue - Chart.BottomY;
            var percent = diff / topToBottomDiff;

            foreach (var indicator in indicators)
            {
                try
                {
                    var indicatorChartTopToBottomDiff = indicator.Value.Chart.TopY - indicator.Value.Chart.BottomY;
                    var yValue = indicator.Value.Chart.BottomY + (indicatorChartTopToBottomDiff * percent);

                    indicator.Value.ShowCrosshair(mouseEventArgs.TimeValue, yValue, mouseEventArgs.CtrlKey);
                }
                catch (Exception)
                {
                    IndicatorInstanceContainer instanceContainer;

                    _indicatorInstances.TryRemove(indicator.Key, out instanceContainer);
                }
            }
        }

        private void TriggerOnMouseDownOnCharts()
        {
            var indicators = GetIndicators();

            foreach (var indicator in indicators)
            {
                try
                {
                    indicator.Value.OnMouseDown();
                }
                catch (Exception)
                {
                    IndicatorInstanceContainer instanceContainer;

                    _indicatorInstances.TryRemove(indicator.Key, out instanceContainer);
                }
            }
        }
    }

    public enum Mode
    {
        All,
        TimeFrame,
        Symbol
    }

    public class IndicatorInstanceContainer
    {
        private readonly WeakReference _indicatorWeakReference;

        public IndicatorInstanceContainer(SynchronizedCrosshair indicator)
        {
            _indicatorWeakReference = new WeakReference(indicator);
        }

        public DateTime? TimeToScroll { get; set; }

        public bool GetIndicator(out SynchronizedCrosshair indicator)
        {
            if (_indicatorWeakReference.IsAlive)
            {
                indicator = (SynchronizedCrosshair)_indicatorWeakReference.Target;

                return true;
            }

            indicator = null;

            return false;
        }
    }

    public class DataBoxControl : CustomControl
    {
        private readonly Grid _panel = new Grid(4, 2);

        private readonly TextBox _timeTextBox = new TextBox();

        private readonly TextBox _pipsTextBox = new TextBox();

        private readonly TextBox _periodsTextBox = new TextBox();

        private readonly TextBox _priceTextBox = new TextBox();

        public DataBoxControl()
        {
            _panel.AddChild(new TextBox { Text = "Time" }, 0, 0);
            _panel.AddChild(_timeTextBox, 0, 1);

            _panel.AddChild(new TextBox { Text = "Pips" }, 1, 0);
            _panel.AddChild(_pipsTextBox, 1, 1);

            _panel.AddChild(new TextBox { Text = "Periods" }, 2, 0);
            _panel.AddChild(_periodsTextBox, 2, 1);

            _panel.AddChild(new TextBox { Text = "Price" }, 3, 0);
            _panel.AddChild(_priceTextBox, 3, 1);

            AddChild(_panel);
        }

        public string Time
        {
            set
            {
                _timeTextBox.Text = value;
            }
            get
            {
                return _timeTextBox.Text;
            }
        }

        public string Pips
        {
            set
            {
                _pipsTextBox.Text = value;
            }
            get
            {
                return _pipsTextBox.Text;
            }
        }

        public string Periods
        {
            set
            {
                _periodsTextBox.Text = value;
            }
            get
            {
                return _periodsTextBox.Text;
            }
        }

        public string Price
        {
            set
            {
                _priceTextBox.Text = value;
            }
            get
            {
                return _priceTextBox.Text;
            }
        }
    }
}