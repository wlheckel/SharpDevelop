﻿// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using ICSharpCode.WpfDesign.Extensions;
using ICSharpCode.WpfDesign.Adorners;
using ICSharpCode.WpfDesign.Designer.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ICSharpCode.WpfDesign.Designer.UIExtensions;
using DragListener = ICSharpCode.WpfDesign.Designer.Controls.DragListener;
using System.Windows.Data;
using System.ComponentModel;
using System.Globalization;

namespace ICSharpCode.WpfDesign.Designer.Extensions
{
	/// <summary>
	/// Description of PathHandlerExtension.
	/// </summary>
	[ExtensionFor(typeof(Path))]
	internal class PathHandlerExtension : LineExtensionBase, IKeyDown, IKeyUp
	{
		enum PathPartConvertType
		{
			insertPoint,
			ToLineSegment,
			ToBezierSegment,
			ToQuadricBezierSegment,
			ToArcSegment,
		}

		protected class PathPoint : INotifyPropertyChanged
		{
			public PathPoint(Point point, Object @object, Object parentObject, Action<Point> setLambda)
			{
				this._point = point;
				this._setLambda = setLambda;
				this.Object = @object;
				this.ParentObject = parentObject;
			}

			private Point _point;
			Action<Point> _setLambda;

			public Point Point
			{
				get { return _point; }
				set {
					if (_setLambda != null)
					{
						_point = value;
						_setLambda(value);
						if (PropertyChanged != null)
							PropertyChanged(this, new PropertyChangedEventArgs("Point"));
					}
				}
			}

			public Point ReferencePoint { get; private set; }

			public object Object { get; private set; }

			public object ParentObject { get; private set; }

			public int PolyLineIndex { get; set; }

			public PathPoint TargetPathPoint { get; set; }

			public event PropertyChangedEventHandler PropertyChanged;
		}

		protected class PathThumb : PointThumb
		{
			public PathThumb(Point point, int index, PathPoint pathpoint) : base(point)
			{
				this.Index = index;
				this.PathPoint = pathpoint;
				var bnd = new Binding("Point") { Source = this.PathPoint, Mode=BindingMode.OneWay };
				this.SetBinding(PointProperty, bnd);
			}

			public int Index { get; set; }

			public PathPoint PathPoint { get; set; }
		}

		protected class RelativeToPointConverter : IValueConverter
		{
			PathPoint pathPoint;
			public RelativeToPointConverter(PathPoint pathPoint)
			{
				this.pathPoint = pathPoint;
			}
			public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
			{
				var pt = (Point)value;
				return pt - new Vector(pathPoint.Point.X - 3.5, pathPoint.Point.Y - 3.5);
			}

			public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
			{
				throw new NotImplementedException();
			}
		}

		private readonly Dictionary<int, Bounds> _selectedThumbs = new Dictionary<int, Bounds>();
		private bool _isDragging;
		ZoomControl _zoom;

		private Control[] segmentContextMenu = null;
		private Control[] arcSegmentContextMenu = null;

		private List<PathPoint> pathPoints = null;

		public PathHandlerExtension()
		{
			var mnu0 = new MenuItem() { Header = "insert Point" };

			var mnu1 = new MenuItem() { Header = "to Line Segment" };
			mnu1.Click += (s, e) => ConvertPart(((DependencyObject)s).TryFindParent<PathThumb>(), PathPartConvertType.ToLineSegment);
			var mnu2 = new MenuItem() {Header = "to Bezier Segment"};
			mnu2.Click += (s, e) => ConvertPart(((DependencyObject)s).TryFindParent<PathThumb>(), PathPartConvertType.ToBezierSegment);
			var mnu3 = new MenuItem() {Header = "to Quadric Bezier Segment"};
			mnu3.Click += (s, e) => ConvertPart(((DependencyObject)s).TryFindParent<PathThumb>(), PathPartConvertType.ToQuadricBezierSegment);
			var mnu4 = new MenuItem() { Header = "to Arc Segment" };
			mnu4.Click += (s, e) => ConvertPart(((DependencyObject)s).TryFindParent<PathThumb>(), PathPartConvertType.ToArcSegment);

			var mnu5 = new MenuItem() { Header = "is Stroked", IsChecked = true };
			var mnu6 = new MenuItem() { Header = "is Smooth Join", IsChecked = true };


			var mnu7 = new MenuItem() { Header = "is large Arc", IsChecked = true };
			var mnu8 = new MenuItem() { Header = "Rotation Angle", IsChecked = true };
			var mnu9 = new MenuItem() { Header = "SweepDirection", IsChecked = true };
			
			segmentContextMenu = new Control[] {mnu0, new Separator(), mnu1, mnu2, mnu3, mnu4, new Separator(), mnu5, mnu6};
			arcSegmentContextMenu = new Control[] { new Separator(), mnu7, mnu8, mnu9 };
		}

		#region thumb methods
		protected DesignerThumb CreateThumb(PlacementAlignment alignment, Cursor cursor, int index, PathPoint pathpoint)
		{
			var point = pathpoint.Point;
			var transform = ((Shape)this.ExtendedItem.View).RenderedGeometry.Transform;
			point = transform.Transform(point);

			var designerThumb = new PathThumb(point, index, pathpoint) {Cursor = cursor};

			if (pathpoint.TargetPathPoint == null && (pathpoint.Object is LineSegment || pathpoint.Object is PolyLineSegment || pathpoint.Object is BezierSegment || pathpoint.Object is QuadraticBezierSegment || pathpoint.Object is ArcSegment)) {
				designerThumb.OperationMenu = segmentContextMenu;
			}

			if (pathpoint.TargetPathPoint != null) {
				designerThumb.IsEllipse = true;
				designerThumb.Foreground = Brushes.Blue;

				var bnd = new Binding("Point") { Source = pathpoint.TargetPathPoint, Mode = BindingMode.OneWay, Converter = new RelativeToPointConverter(pathpoint) };
				designerThumb.SetBinding(PathThumb.RelativeToPointProperty, bnd);
			}

			AdornerPanel.SetPlacement(designerThumb, designerThumb.AdornerPlacement);
			adornerPanel.Children.Add(designerThumb);

			DragListener drag = new DragListener(designerThumb);

			WeakEventManager<DesignerThumb, MouseButtonEventArgs>.AddHandler(designerThumb, "PreviewMouseLeftButtonDown", ResizeThumbOnMouseLeftButtonUp);

			drag.MouseDown += drag_MouseDown;
			drag.Started += drag_Started;
			drag.Changed += drag_Changed;
			drag.Completed += drag_Completed;
			return designerThumb;
		}

		private void ConvertPart(PathThumb senderThumb, PathPartConvertType convertType)
		{
			if (senderThumb.PathPoint.ParentObject is PathFigure)
			{
				var pathFigure = senderThumb.PathPoint.ParentObject as PathFigure;
				var pathSegment = senderThumb.PathPoint.Object as PathSegment;

				var idx = pathFigure.Segments.IndexOf(pathSegment);

				var point = senderThumb.PathPoint.Point;

				if (pathSegment is PolyLineSegment)
				{
					var poly = pathSegment as PolyLineSegment;
					var lst = poly.Points.Take(senderThumb.PathPoint.PolyLineIndex);
					var lst2 = poly.Points.Skip(senderThumb.PathPoint.PolyLineIndex + 1);
					pathFigure.Segments.RemoveAt(idx);
					var p1 = new PolyLineSegment();
					p1.Points.AddRange(lst);
					pathFigure.Segments.Insert(idx, p1);
					pathFigure.Segments.Insert(idx+1, new LineSegment());
					var p2 = new PolyLineSegment();
					p2.Points.AddRange(lst2);
					pathFigure.Segments.Insert(idx+2, p2);
					idx++;
				}

				pathFigure.Segments.RemoveAt(idx);

				PathSegment newSegment = null;
				switch (convertType)
				{
					case PathPartConvertType.ToBezierSegment:
						newSegment = new BezierSegment() { Point1 = point - new Vector(40, 40), Point2 = point + new Vector(-40, 40), Point3 = point };
						break;
					case PathPartConvertType.ToQuadricBezierSegment:
						newSegment = new QuadraticBezierSegment() { Point1 = point - new Vector(40, 40), Point2 = point  };
						break;
					case PathPartConvertType.ToArcSegment:
						newSegment = new ArcSegment() { Point = point, Size = new Size(20, 20) };
						break;
					default:
						newSegment = new LineSegment() { Point = point };
						break;
				}

				pathFigure.Segments.Insert(idx, newSegment);
			}

			this.ExtendedItem.ReapplyAllExtensions();
		}

		private void ResetThumbs()
		{
			foreach (FrameworkElement rt in adornerPanel.Children)
			{
				if (rt is DesignerThumb)
					(rt as DesignerThumb).IsPrimarySelection = true;
			}
			_selectedThumbs.Clear();
		}

		private void SelectThumb(PathThumb mprt)
		{
			var points = GetPoints();
			Point p = points[mprt.Index].Point;
			_selectedThumbs.Add(mprt.Index, new Bounds { X = p.X, Y = p.Y });

			mprt.IsPrimarySelection = false;
		}

		#endregion

		#region eventhandlers

		private void ResizeThumbOnMouseLeftButtonUp(object sender, MouseButtonEventArgs mouseButtonEventArgs)
		{
			//get current thumb
			var mprt = sender as PathThumb;
			if (mprt != null)
			{
				////shift+ctrl will remove selected point
				//if ((Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) &&
				//    (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
				//{
				//	//unselect all points
				//	ResetThumbs();
				
				//	//iterate thumbs to lower index of remaining thumbs
				//	foreach (PathThumb m in adornerPanel.Children) {
				//		if (m.Index > mprt.Index)
				//			m.Index--;
				//	}

				//                //remove point and thumb
				//                pathPoints.RemoveAt(mprt.Index);
				//	adornerPanel.Children.Remove(mprt);

				//	Invalidate();
				//}
				//else
				{
					//if not keyboard ctrl is pressed and selected point is not previously selected, clear selection
					if (!_selectedThumbs.ContainsKey(mprt.Index) & !Keyboard.IsKeyDown(Key.LeftCtrl) &
					    !Keyboard.IsKeyDown(Key.RightCtrl))
					{
						ResetThumbs();
					}
					//add selected thumb, if ctrl pressed this could be all points in poly
					if (!_selectedThumbs.ContainsKey(mprt.Index))
						SelectThumb(mprt);
					_isDragging = false;
				}
			}
		}

		protected void drag_MouseDown(DragListener drag)
		{
			
		}
		
		// TODO : Remove all hide/show extensions from here.
		protected void drag_Started(DragListener drag)
		{
			//get current thumb
			var mprt = (drag.Target as PathThumb);
			if (mprt != null)
			{
				SetOperation();
			}
		}

		void SetOperation()
		{
			var designPanel = ExtendedItem.Services.DesignPanel as DesignPanel;
			_zoom = designPanel.TryFindParent<ZoomControl>();
			
			if (resizeBehavior != null)
				operation = PlacementOperation.Start(extendedItemArray, PlacementType.MovePoint);
			else
			{
				changeGroup = ExtendedItem.Context.OpenGroup("Resize", extendedItemArray);
			}
			_isResizing = true;
		}

		void ChangeOperation(List<PathPoint> points)
		{
			//this is for SharpDevelop built in undo functionality
			if (operation != null)
			{
				var info = operation.PlacedItems[0];
				var result = info.OriginalBounds;

				IEnumerable<double> xs = points.Select(x => x.Point.X);
				IEnumerable<double> ys = points.Select(y => y.Point.Y);
				result.X = (double)(info.Item.Properties.GetAttachedProperty(Canvas.LeftProperty).ValueOnInstance);
				result.Y = (double)(info.Item.Properties.GetAttachedProperty(Canvas.TopProperty).ValueOnInstance);
				result.Width = xs.Max() - xs.Min();
				result.Height = ys.Max() - ys.Min();

				info.Bounds = result.Round();
				operation.CurrentContainerBehavior.BeforeSetPosition(operation);
				operation.CurrentContainerBehavior.SetPosition(info);
			}
		}

		void CommitOperation()
		{
			if (operation != null)
			{
//				foreach (int i in _selectedThumbs.Keys)
//				{
//					_selectedThumbs[i].X = points[i].X;
//					_selectedThumbs[i].Y = points[i].Y;
//				}
				
				//ExtendedItem.Properties.GetProperty(pl != null ? Polyline.PointsProperty : Polygon.PointsProperty).SetValue(points);
				
				operation.Commit();

				operation = null;
			}
			else
			{
				if (changeGroup != null)
					changeGroup.Commit();
				changeGroup = null;
			}
			_isResizing = false;

			Invalidate();
		}

		protected void drag_Changed(DragListener drag)
		{
			var mprt = drag.Target as PathThumb;
			if (mprt != null)
			{
				double dx = 0;
				double dy = 0;
				//if has zoomed
				if (_zoom != null)
				{
					dx = drag.Delta.X * (1 / _zoom.CurrentZoom);
					dy = drag.Delta.Y * (1 / _zoom.CurrentZoom);
				}
				
				_isDragging = true;
				MovePoints(pathPoints, dx, dy);
			}
			ChangeOperation(pathPoints);
		}

		protected void drag_Completed(DragListener drag)
		{
			var mprt = drag.Target as PathThumb;
			if (mprt != null)
			{
				if (operation != null && drag.IsCanceled)
				{
					operation.Abort();
				}
				else if (drag.IsCanceled)
				{
					changeGroup.Abort();
				}
				CommitOperation();
			}
		}



		protected override void OnInitialized()
		{
			base.OnInitialized();

			pathPoints = GetPoints();

			resizeThumbs = new List<DesignerThumb>();
			for (int i = 0; i < pathPoints.Count; i++)
			{
				CreateThumb(PlacementAlignment.BottomRight, Cursors.Cross, i, pathPoints[i]);
			}

			Invalidate();

			ResetThumbs();
			_isDragging = false;

			extendedItemArray[0] = ExtendedItem;
			ExtendedItem.PropertyChanged += OnPropertyChanged;
			resizeBehavior = PlacementOperation.GetPlacementBehavior(extendedItemArray);
			UpdateAdornerVisibility();
		}

		#endregion

		List<PathPoint> GetPoints()
		{
			return GetPoints(this.ExtendedItem.View as Path);
		}
		
		static List<PathPoint> GetPoints(Path path)
		{
			var retVal = new List<PathPoint>();
			AddGeometryPoints(retVal, path.Data);
			
			return retVal;
		}

		private static void AddGeometryPoints(List<PathPoint> list, Geometry geometry)
		{
			if (geometry is CombinedGeometry) {
				var g = geometry as CombinedGeometry;
				AddGeometryPoints(list, g.Geometry1);
				AddGeometryPoints(list, g.Geometry2);
			} else if (geometry is GeometryGroup)
			{
				var gg = geometry as GeometryGroup;
				foreach (var g in gg.Children) {
					AddGeometryPoints(list, g);
				}
			} else if (geometry is PathGeometry) {
				var g = geometry as PathGeometry;
				if (geometry!=null) {
					foreach(var figure in g.Figures) {
						list.Add(new PathPoint(figure.StartPoint, figure, null, (p) => figure.StartPoint = p));
						foreach (var s in figure.Segments) {
							if (s is LineSegment)
								list.Add(new PathPoint(((LineSegment)s).Point, s, figure, (p) => ((LineSegment)s).Point = p));
							else if (s is PolyLineSegment)
							{
								var poly = s as PolyLineSegment;
								for (int n = 0; n < poly.Points.Count; n++)
								{
									var closure_n = n;
									list.Add(new PathPoint(poly.Points[closure_n], s, figure, (p) => poly.Points[closure_n] = p) { PolyLineIndex = closure_n });
								}
							}
							else if (s is BezierSegment)
							{
								var pathp = new PathPoint(((BezierSegment)s).Point3, s, figure, (p) => ((BezierSegment)s).Point3 = p);
								var previous = list.Last();
								list.Add(new PathPoint(((BezierSegment)s).Point1, s, figure, (p) => ((BezierSegment)s).Point1 = p) { TargetPathPoint = previous });
								list.Add(new PathPoint(((BezierSegment)s).Point2, s, figure, (p) => ((BezierSegment)s).Point2 = p) { TargetPathPoint = pathp });
								list.Add(pathp);
							}
							else if (s is QuadraticBezierSegment)
							{
								var pathp = new PathPoint(((QuadraticBezierSegment)s).Point2, s, figure, (p) => ((QuadraticBezierSegment)s).Point2 = p);
								list.Add(new PathPoint(((QuadraticBezierSegment)s).Point1, s, figure, (p) => ((QuadraticBezierSegment)s).Point1 = p) { TargetPathPoint = pathp });
								list.Add(pathp);
							}
							else if (s is ArcSegment)
							{
								var arc = ((ArcSegment)s);
								var pathp = new PathPoint(arc.Point, s, figure, (p) => arc.Point = p);
								list.Add(new PathPoint(arc.Point - new Vector(arc.Size.Width, arc.Size.Height), s, figure, (p) => arc.Size = new Size(Math.Abs(arc.Point.X - p.X), Math.Abs(arc.Point.Y - p.Y))) { TargetPathPoint = pathp });
								list.Add(pathp);
							}
						}
					}
				}
			} else if (geometry is RectangleGeometry) {
				var g = geometry as RectangleGeometry;
				list.Add(new PathPoint(g.Rect.TopLeft, geometry, null, null)); //(p) => g.Rect.Left = p.X));
				list.Add(new PathPoint(g.Rect.TopRight, geometry, null, null)); //(p) => g.Rect.Width = p.X));
				list.Add(new PathPoint(g.Rect.BottomLeft, geometry, null, null)); //(p) => g.Rect.Top = p.Y));
				list.Add(new PathPoint(g.Rect.BottomRight, geometry, null, null)); //(p) => g.Rect.Height = p.Y));
			} else if (geometry is EllipseGeometry) {
				var g = geometry as EllipseGeometry;
				list.Add(new PathPoint(g.Center, geometry, null, (p) => g.Center = p));
			} else if (geometry is LineGeometry) {
				var g = geometry as LineGeometry;
				list.Add(new PathPoint(g.StartPoint, geometry, null, (p) => g.StartPoint = p));
				list.Add(new PathPoint(g.EndPoint, geometry, null, (p) => g.EndPoint = p));
			}
		}
		
		List<PathPoint> MovePoints(List<PathPoint> pc, double displacementX, double displacementY)
		{
			//iterate all selected points
			foreach (int i in _selectedThumbs.Keys)
			{
				Point p = pc[i].Point;

				//x and y is calculated from the currentl point
				double x = _selectedThumbs[i].X + displacementX;
				double y = _selectedThumbs[i].Y + displacementY;

				p.X = x;
				p.Y = y;
				pc[i].Point = p;
			}
			return pc;
		}

		#region IKeyDown

		public bool InvokeDefaultAction
		{
			get { return _selectedThumbs.Count == 0 || _selectedThumbs.Count == pathPoints.Count - 1; }
		}

		int _movingDistance;
		public void KeyDownAction(object sender, KeyEventArgs e)
		{
			Debug.WriteLine("KeyDown");
			if (IsArrowKey(e.Key))
				if (operation == null)
			{
				SetOperation();
				_movingDistance = 0;
			}


			var dx1 = (e.Key == Key.Left) ? Keyboard.IsKeyDown(Key.LeftShift) ? _movingDistance - 10 : _movingDistance - 1 : 0;
			var dy1 = (e.Key == Key.Up) ? Keyboard.IsKeyDown(Key.LeftShift) ? _movingDistance - 10 : _movingDistance - 1 : 0;
			var dx2 = (e.Key == Key.Right) ? Keyboard.IsKeyDown(Key.LeftShift) ? _movingDistance + 10 : _movingDistance + 1 : 0;
			var dy2 = (e.Key == Key.Down) ? Keyboard.IsKeyDown(Key.LeftShift) ? _movingDistance + 10 : _movingDistance + 1 : 0;

			ChangeOperation(MovePoints(pathPoints, dx1 + dx2, dy1 + dy2));
			_movingDistance = (dx1 + dx2 + dy1 + dy2);
		}

		public void KeyUpAction(object sender, KeyEventArgs e)
		{
			Debug.WriteLine("Keyup");
			if (IsArrowKey(e.Key))
				CommitOperation();
		}

		bool IsArrowKey(Key key)
		{
			return (key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down);
		}
		#endregion
	}
}
