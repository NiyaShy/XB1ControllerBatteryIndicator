﻿using System;
using System.Windows;
using System.Windows.Media;
using Caliburn.Micro;

namespace XB1ControllerBatteryIndicator.BatteryPopup
{
	public class SimpleBatteryLevelPopupViewModel : PropertyChangedBase, IBatteryLevelPopupViewModel
	{
		private string _batteryLevel;
		private TimeSpan _displayDuration;
		private Point _position;
		private CornerRadius _cornerRadius;
		private SolidColorBrush _background;
		private SolidColorBrush _foregroundColor;
		private Thickness _borderSize;
		private string _controllerName;
		private double _fontSize;
		private Size _size;

		public string ControllerName
		{
			get { return _controllerName; }
			set { Set(ref _controllerName, value); }
		}

		public string BatteryLevel
		{
			get { return _batteryLevel; }
			set { Set(ref _batteryLevel, value); }
		}

		public TimeSpan DisplayDuration
		{
			get { return _displayDuration; }
			set { Set(ref _displayDuration, value); }
		}

		public Point Position
		{
			get { return _position; }
			set { Set(ref _position, value); }
		}

		public Size Size
		{
			get { return _size; }
			set
			{
				if (Set(ref _size, value))
					CornerRadius = new CornerRadius(Size.Height / 2);
			}
		}

		public CornerRadius CornerRadius
		{
			get { return _cornerRadius; }
			private set { Set(ref _cornerRadius, value); }
		}

		public SolidColorBrush Background
		{
			get { return _background; }
			set { Set(ref _background, value); }
		}

		public SolidColorBrush ForegroundColor
		{
			get { return _foregroundColor; }
			set { Set(ref _foregroundColor, value); }
		}

		public Thickness BorderSize
		{
			get { return _borderSize; }
			set { Set(ref _borderSize, value); }
		}

		public double FontSize
		{
			get { return _fontSize; }
			set { Set(ref _fontSize, value); }
		}
	}
}
