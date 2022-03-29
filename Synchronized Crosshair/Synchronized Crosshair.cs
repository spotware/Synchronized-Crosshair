using cAlgo.API;
using System.Collections.Concurrent;
using System;
using System.Collections.Generic;
using System.Threading;

namespace cAlgo
{
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class SynchronizedCrosshair : Indicator
    {
        private static ConcurrentDictionary<string, IndicatorInstanceContainer> _indicatorInstances = new ConcurrentDictionary<string, IndicatorInstanceContainer>();

        private static int _numberOfChartsToScroll;

        private DateTime _lastScrollTime;

        private string _chartKey;

        private string _horizontalLineObjectName;

        private string _verticalLineObjectName;

        private string _lineObjectName;

        private ChartHorizontalLine _horizontalLine;

        private ChartVerticalLine _verticalLine;

        private ChartTrendLine _line;

        private DateTime _ctrlKeyUpTime;

        private double _ctrlKeyUpPrice;

        private bool _isActive;

        private bool _isCtrlKeyUp;

        [Parameter("Mode", DefaultValue = Mode.All, Group = "General")]
        public Mode Mode { get; set; }

        protected override void Initialize()
        {
            _chartKey = string.Format("{0}_{1}_{2}", SymbolName, TimeFrame, Chart.ChartType);
            _horizontalLineObjectName = string.Format("{0}_Horizontal", _chartKey);
            _verticalLineObjectName = string.Format("{0}_Vertical", _chartKey);
            _lineObjectName = string.Format("{0}_Line", _chartKey);

            IndicatorInstanceContainer oldIndicatorContainer;

            GetIndicatorInstanceContainer(_chartKey, out oldIndicatorContainer);

            _indicatorInstances.AddOrUpdate(_chartKey, new IndicatorInstanceContainer(this), (key, value) => new IndicatorInstanceContainer(this));

            if (oldIndicatorContainer != null && oldIndicatorContainer.TimeToScroll.HasValue)
            {
                //ScrollXTo(oldIndicatorContainer.Data.Value);
            }

            //Chart.ScrollChanged += Chart_ScrollChanged;
            Chart.MouseMove += Chart_MouseMove;
            Chart.MouseDown += Chart_MouseDown;
        }

        private void Chart_MouseDown(ChartMouseEventArgs obj)
        {
            if (_isActive == false) return;

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
        }

        private void Chart_MouseMove(ChartMouseEventArgs obj)
        {
            IndicatorInstanceContainer indicatorContainer;

            if (GetIndicatorInstanceContainer(_chartKey, out indicatorContainer) == false) return;

            if (_isActive && obj.CtrlKey == false)
            {
                if (_isCtrlKeyUp == false)
                {
                    _isCtrlKeyUp = true;

                    _ctrlKeyUpTime = obj.TimeValue;
                    _ctrlKeyUpPrice = obj.YValue;

                    return;
                }

                if (_line == null)
                {
                    _line = Chart.DrawTrendLine(_lineObjectName, _ctrlKeyUpTime, _ctrlKeyUpPrice, obj.TimeValue, obj.YValue, Chart.ColorSettings.ForegroundColor);
                }
                else
                {
                    _line.Time2 = obj.TimeValue;
                    _line.Y2 = obj.YValue;
                }
            }
            else if (obj.CtrlKey)
            {
                _isCtrlKeyUp = false;
                _isActive = true;

                if (_horizontalLine == null)
                {
                    _horizontalLine = Chart.DrawHorizontalLine(_horizontalLineObjectName, obj.YValue, Chart.ColorSettings.ForegroundColor);
                }
                else
                {
                    _horizontalLine.Y = obj.YValue;
                }

                if (_verticalLine == null)
                {
                    _verticalLine = Chart.DrawVerticalLine(_verticalLineObjectName, obj.TimeValue, Chart.ColorSettings.ForegroundColor);
                }
                else
                {
                    _verticalLine.Time = obj.TimeValue;
                }
            }
        }

        public override void Calculate(int index)
        {
        }

        public void ScrollXTo(DateTime time)
        {
            IndicatorInstanceContainer indicatorContainer;

            if (GetIndicatorInstanceContainer(_chartKey, out indicatorContainer))
            {
                indicatorContainer.TimeToScroll = time;
            }

            if (Bars[0].OpenTime > time)
            {
                LoadMoreHistory();
            }
            else
            {
                Chart.ScrollXTo(time);
            }
        }

        private void LoadMoreHistory()
        {
            var numberOfLoadedBars = Bars.LoadMoreHistory();

            if (numberOfLoadedBars == 0)
            {
                Chart.DrawStaticText("ScrollError", "Synchronized Crosshair: Can't load more data to keep in sync with other charts as more historical data is not available for this chart", VerticalAlignment.Bottom, HorizontalAlignment.Left, Color.Red);
            }
        }

        private void Chart_ScrollChanged(ChartScrollEventArgs obj)
        {
            IndicatorInstanceContainer indicatorContainer;

            if (GetIndicatorInstanceContainer(_chartKey, out indicatorContainer))
            {
                indicatorContainer.TimeToScroll = null;
            }

            if (_numberOfChartsToScroll > 0)
            {
                Interlocked.Decrement(ref _numberOfChartsToScroll);

                return;
            }

            var firstBarTime = obj.Chart.Bars.OpenTimes[obj.Chart.FirstVisibleBarIndex];

            if (_lastScrollTime == firstBarTime) return;

            _lastScrollTime = firstBarTime;

            switch (Mode)
            {
                case Mode.Symbol:
                    ScrollCharts(firstBarTime, indicator => indicator.SymbolName.Equals(SymbolName, StringComparison.Ordinal));
                    break;

                case Mode.TimeFrame:
                    ScrollCharts(firstBarTime, indicator => indicator.TimeFrame == TimeFrame);
                    break;

                default:
                    ScrollCharts(firstBarTime);
                    break;
            }
        }

        private void ScrollCharts(DateTime firstBarTime, Func<Indicator, bool> predicate = null)
        {
            var toScroll = new List<SynchronizedCrosshair>(_indicatorInstances.Values.Count);

            foreach (var indicatorContianer in _indicatorInstances)
            {
                SynchronizedCrosshair indicator;

                if (indicatorContianer.Value.GetIndicator(out indicator) == false || indicator == this || (predicate != null && predicate(indicator) == false)) continue;

                toScroll.Add(indicator);
            }

            Interlocked.CompareExchange(ref _numberOfChartsToScroll, toScroll.Count, _numberOfChartsToScroll);

            foreach (var indicator in toScroll)
            {
                try
                {
                    indicator.ScrollXTo(firstBarTime);
                }
                catch (Exception)
                {
                    Interlocked.Decrement(ref _numberOfChartsToScroll);
                }
            }
        }

        private bool GetIndicatorInstanceContainer(string chartKey, out IndicatorInstanceContainer indicatorContainer)
        {
            if (_indicatorInstances.TryGetValue(chartKey, out indicatorContainer))
            {
                return true;
            }

            indicatorContainer = null;

            return false;
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
}