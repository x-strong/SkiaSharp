﻿using System;
using Windows.ApplicationModel;
using Windows.Graphics.Display;

#if WINDOWS
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
#else
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
#endif

#if WINDOWS
namespace SkiaSharp.Views.Windows
#else
namespace SkiaSharp.Views.UWP
#endif
{
	public partial class SKXamlCanvas : Canvas
	{
		private const float DpiBase = 96.0f;

		private static readonly DependencyProperty ProxyVisibilityProperty =
			DependencyProperty.Register(
				"ProxyVisibility",
				typeof(Visibility),
				typeof(SKXamlCanvas),
				new PropertyMetadata(Visibility.Visible, OnVisibilityChanged));

		private static bool designMode = DesignMode.DesignModeEnabled;

		private IntPtr pixels;
		private WriteableBitmap bitmap;
		private bool ignorePixelScaling;
		private bool isVisible = true;

		// workaround for https://github.com/mono/SkiaSharp/issues/1118
		private int loadUnloadCounter = 0;

		public SKXamlCanvas()
		{
			if (designMode)
				return;

#if !WINDOWS
			var display = DisplayInformation.GetForCurrentView();
			OnDpiChanged(display);
#endif

			Loaded += OnLoaded;
			Unloaded += OnUnloaded;
			SizeChanged += OnSizeChanged;

			var binding = new Binding
			{
				Path = new PropertyPath(nameof(Visibility)),
				Source = this
			};
			SetBinding(ProxyVisibilityProperty, binding);
		}

		public SKSize CanvasSize { get; private set; }

		public bool IgnorePixelScaling
		{
			get => ignorePixelScaling;
			set
			{
				ignorePixelScaling = value;
				Invalidate();
			}
		}

		public double Dpi { get; private set; } = 1;

		public event EventHandler<SKPaintSurfaceEventArgs> PaintSurface;

		protected virtual void OnPaintSurface(SKPaintSurfaceEventArgs e)
		{
			PaintSurface?.Invoke(this, e);
		}

		private static void OnVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is SKXamlCanvas canvas && e.NewValue is Visibility visibility)
			{
				canvas.isVisible = visibility == Visibility.Visible;
				canvas.Invalidate();
			}
		}

#if WINDOWS
		private void OnXamlRootChanged(XamlRoot xamlRoot = null, XamlRootChangedEventArgs e = null)
		{
			var root = xamlRoot ?? XamlRoot;
			Dpi = root?.RasterizationScale ?? 1.0;
			Invalidate();
		}
#else
		private void OnDpiChanged(DisplayInformation sender, object args = null)
		{
			Dpi = sender.LogicalDpi / DpiBase;
			Invalidate();
		}
#endif

		private void OnSizeChanged(object sender, SizeChangedEventArgs e)
		{
			Invalidate();
		}

		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			loadUnloadCounter++;
			if (loadUnloadCounter != 1)
				return;

#if WINDOWS
			XamlRoot.Changed += OnXamlRootChanged;
			OnXamlRootChanged();
#else
			var display = DisplayInformation.GetForCurrentView();
			display.DpiChanged += OnDpiChanged;

			OnDpiChanged(display);
#endif
		}

		private void OnUnloaded(object sender, RoutedEventArgs e)
		{
			loadUnloadCounter--;
			if (loadUnloadCounter != 0)
				return;

#if WINDOWS
			if(XamlRoot != null)
			{
				XamlRoot.Changed -= OnXamlRootChanged;
			}
#else
			var display = DisplayInformation.GetForCurrentView();
			display.DpiChanged -= OnDpiChanged;
#endif

			FreeBitmap();
		}

		public void Invalidate()
		{
#if WINDOWS
			DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, DoInvalidate);
#else
			Dispatcher.RunAsync(CoreDispatcherPriority.Normal, DoInvalidate);
#endif
		}

		private void DoInvalidate()
		{
			if (designMode)
				return;

			if (!isVisible)
				return;

			var info = CreateBitmap(out var unscaledSize, out var dpi);

			if (info.Width <= 0 || info.Height <= 0)
			{
				CanvasSize = SKSize.Empty;
				return;
			}

			var userVisibleSize = IgnorePixelScaling ? unscaledSize : info.Size;
			CanvasSize = userVisibleSize;

			using (var surface = SKSurface.Create(info, pixels, info.RowBytes))
			{
				if (IgnorePixelScaling)
				{
					var canvas = surface.Canvas;
					canvas.Scale(dpi);
					canvas.Save();
				}

				OnPaintSurface(new SKPaintSurfaceEventArgs(surface, info.WithSize(userVisibleSize), info));
			}
			bitmap.Invalidate();
		}

		private SKSizeI CreateSize(out SKSizeI unscaledSize, out float dpi)
		{
			unscaledSize = SKSizeI.Empty;
			dpi = (float)Dpi;

			var w = ActualWidth;
			var h = ActualHeight;

			if (!IsPositive(w) || !IsPositive(h))
				return SKSizeI.Empty;

			unscaledSize = new SKSizeI((int)w, (int)h);
			return new SKSizeI((int)(w * dpi), (int)(h * dpi));

			static bool IsPositive(double value)
			{
				return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
			}
		}

		private SKImageInfo CreateBitmap(out SKSizeI unscaledSize, out float dpi)
		{
			var size = CreateSize(out unscaledSize, out dpi);
			var info = new SKImageInfo(size.Width, size.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);

			if (bitmap?.PixelWidth != info.Width || bitmap?.PixelHeight != info.Height)
				FreeBitmap();

			if (bitmap == null && info.Width > 0 && info.Height > 0)
			{
				bitmap = new WriteableBitmap(info.Width, info.Height);
				pixels = bitmap.GetPixels();

				var brush = new ImageBrush
				{
					ImageSource = bitmap,
					AlignmentX = AlignmentX.Left,
					AlignmentY = AlignmentY.Top,
					Stretch = Stretch.Fill
				};
				Background = brush;
			}

			return info;
		}

		private void FreeBitmap()
		{
			Background = null;
			bitmap = null;
			pixels = IntPtr.Zero;
		}
	}
}
