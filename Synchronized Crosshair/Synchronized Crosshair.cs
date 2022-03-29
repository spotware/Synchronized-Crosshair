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

        [Parameter("Mode", DefaultValue = Mode.All, Group = "General")]
        public Mode Mode { get; set; }

        protected override void Initialize()
        {
            _chartKey = GetChartKey(this);

            IndicatorInstanceContainer oldIndicatorContainer;

            GetIndicatorInstanceContainer(_chartKey, out oldIndicatorContainer);

            _indicatorInstances.AddOrUpdate(_chartKey, new IndicatorInstanceContainer(this), (key, value) => new IndicatorInstanceContainer(this));

            if (oldIndicatorContainer != null && oldIndicatorContainer.TimeToScroll.HasValue)
            {
                //ScrollXTo(oldIndicatorContainer.Data.Value);
            }

            //Chart.ScrollChanged += Chart_ScrollChanged;
            Chart.MouseMove += Chart_MouseMove;
        }

        private void Chart_MouseMove(ChartMouseEventArgs obj)
        {
            IndicatorInstanceContainer indicatorContainer;

            if (GetIndicatorInstanceContainer(_chartKey, out indicatorContainer) == false) return;

            if (obj.CtrlKey == false)
            {
                if (indicatorContainer.CrosshairHorizontalLine != null)
                {
                    Chart.RemoveObject(indicatorContainer.CrosshairHorizontalLineObjectName);
                }

                if (indicatorContainer.CrosshairVerticalLine != null)
                {
                    Chart.RemoveObject(indicatorContainer.CrosshairVerticalLineObjectName);
                }

                return;
            }

            if (indicatorContainer.CrosshairHorizontalLine == null)
            {
                indicatorContainer.CrosshairHorizontalLine = Chart.DrawHorizontalLine(indicatorContainer.CrosshairHorizontalLineObjectName, obj.YValue, Chart.ColorSettings.ForegroundColor);
            }
            else
            {
                indicatorContainer.CrosshairHorizontalLine.Y = obj.YValue;
            }

            if (indicatorContainer.CrosshairVerticalLine == null)
            {
                indicatorContainer.CrosshairVerticalLine = Chart.DrawVerticalLine(indicatorContainer.CrosshairVerticalLineObjectName, obj.TimeValue, Chart.ColorSettings.ForegroundColor);
            }
            else
            {
                indicatorContainer.CrosshairVerticalLine.Time = obj.TimeValue;
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

        private string GetChartKey(Indicator indicator)
        {
            return string.Format("{0}_{1}_{2}", indicator.SymbolName, indicator.TimeFrame, indicator.Chart.ChartType);
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

        private readonly string _key;

        private readonly string _crosshairHorizontalLineObjectName;

        private readonly string _crosshairVerticalLineObjectName;

        public IndicatorInstanceContainer(SynchronizedCrosshair indicator)
        {
            _indicatorWeakReference = new WeakReference(indicator);

            _key = string.Format("{0}_{1}_{2}", indicator.SymbolName, indicator.TimeFrame, indicator.Chart.ChartType);

            _crosshairHorizontalLineObjectName = string.Format("{0}_Horizontal", _key);
            _crosshairVerticalLineObjectName = string.Format("{0}_Vertical", _key);
        }

        public string Key
        {
            get
            {
                return _key;
            }
        }

        public string CrosshairHorizontalLineObjectName
        {
            get
            {
                return _crosshairHorizontalLineObjectName;
            }
        }

        public string CrosshairVerticalLineObjectName
        {
            get
            {
                return _crosshairVerticalLineObjectName;
            }
        }

        public DateTime? TimeToScroll { get; set; }

        public ChartHorizontalLine CrosshairHorizontalLine { get; set; }

        public ChartVerticalLine CrosshairVerticalLine { get; set; }

        public bool IsActive { get; set; }

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