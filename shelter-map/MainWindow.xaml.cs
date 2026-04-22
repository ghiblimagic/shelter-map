using System.Buffers.Text;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml.Linq;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Rasters;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Tasks.Geocoding;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace shelter_map
{
    public partial class MainWindow : Window
    {
        private List<Shelter> _shelters = new List<Shelter>();
        private Shelter? _selectedShelter = null;

        public MainWindow()
        {
            InitializeComponent();
            InitializeMap();
            _ = GeocodeAndPlotShelters();
        }

        private void InitializeMap()
        {
            var map = new Map(BasemapStyle.ArcGISStreets);
            MyMapView.Map = map;
            MyMapView.GeoViewTapped += MyMapView_GeoViewTapped;

            var losAngelesCounty = new Envelope(
                -118.9,
                33.7,
                -117.6,
                34.8,
                SpatialReferences.Wgs84
            );

            MyMapView.SetViewpoint(new Viewpoint(losAngelesCounty));

            MyMapView.ViewpointChanged += (s, e) => RedrawPieCharts();
            MyMapView.SizeChanged += (s, e) => RedrawPieCharts();
        }

        private List<Shelter> LoadShelters()
        {
            var json = File.ReadAllText("shelters.json");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<List<Shelter>>(json, options);
        }

        private async Task GeocodeAndPlotShelters()
        {
            try
            {
                _shelters = LoadShelters();
                var locator = await LocatorTask.CreateAsync(
                    new Uri(
                        "https://geocode-api.arcgis.com/arcgis/rest/services/World/GeocodeServer"
                    )
                );

                var graphicsOverlay = new GraphicsOverlay();
                MyMapView.GraphicsOverlays.Add(graphicsOverlay);

                foreach (var shelter in _shelters)
                {
                    var geocodeParams = new GeocodeParameters { MaxResults = 1 };
                    var results = await locator.GeocodeAsync(shelter.Address, geocodeParams);
                    if (results.Count == 0)
                        continue;

                    shelter.Latitude = results[0].DisplayLocation.Y;
                    shelter.Longitude = results[0].DisplayLocation.X;
                }

                // Zoom to shelter locations
                var points = _shelters
                    .Where(s => s.Latitude != 0 && s.Longitude != 0)
                    .Select(s => new MapPoint(s.Longitude, s.Latitude, SpatialReferences.Wgs84))
                    .ToList();

                if (points.Count > 0)
                {
                    var builder = new EnvelopeBuilder(SpatialReferences.Wgs84);
                    foreach (var point in points)
                        builder.UnionOf(point);

                    // Expand just a little around the shelter points
                    builder.Expand(1.3);
                    MyMapView.SetViewpoint(new Viewpoint(builder.ToGeometry()));
                }

                DrawCircles();
                UpdateLegend();
                // have all panels start as closed, before the first shelter is selected

                HideAllSidebarPanels();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        public class AnimalBreakdown
        {
            public int Dogs { get; set; }
            public int Cats { get; set; }
            public int Kittens { get; set; }
            public int Total => Dogs + Cats + Kittens;
        }

        public class NonLiveBreakdown
        {
            public AnimalBreakdown Euthanasia { get; set; } = new();
            public AnimalBreakdown Died { get; set; } = new();
            public AnimalBreakdown Missing { get; set; } = new();
        }

        // stripes for cats
        private Brush CreateStripeBrush(Color color)
        {
            return new DrawingBrush
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 1, 1), // ⬅️ bigger tile = wider stripes
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                Stretch = Stretch.Fill,

                Drawing = new DrawingGroup
                {
                    Children =
                    {
                        // 1. Solid blue background (geometry is defined in an arbitrary 20x20 space;
                        //    it will be scaled by the brush to the rectangle being painted)
                        // this makes the line look centered
                        new GeometryDrawing(
                            new SolidColorBrush(color),
                            null,
                            new RectangleGeometry(new Rect(0, 0, 20, 20))
                        ),
                        // 2. Horizontal line across the tile (y = 10 in the 20px space -> center after scaling)
                        new GeometryDrawing(
                            null,
                            new Pen(Brushes.White, 2),
                            new LineGeometry(new Point(0, 10), new Point(20, 10))
                        ),
                    },
                },
            };
        }

        // dogs have zigzag
        private Brush CreateZigzagBrush(Color color)
        {
            // Use a simple 45° diagonal white line centered in the tile.
            var group = new DrawingGroup();
            // ensure clipping so tiles tile cleanly
            group.ClipGeometry = new RectangleGeometry(new Rect(0, 0, 20, 20));

            // Background
            group.Children.Add(
                new GeometryDrawing(
                    new SolidColorBrush(color),
                    null,
                    new RectangleGeometry(new Rect(0, 0, 20, 20))
                )
            );

            // Diagonal white line from bottom-left to top-right (centered at 45°)
            // Use a Pen with desired thickness; the geometry is drawn in 20x20 space and will scale.
            group.Children.Add(
                new GeometryDrawing(
                    null,
                    new Pen(Brushes.White, 2),
                    new LineGeometry(new Point(2, 18), new Point(18, 2))
                )
            );

            return new DrawingBrush
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 1, 1),
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                Stretch = Stretch.Fill,
                Drawing = group,
            };
        }

        private System.Windows.Media.Brush GetNonLiveColor(string animal, string outcome)
        {
            Color baseColor = outcome switch
            {
                "euth" => Color.FromRgb(0, 90, 180), // blue
                "died" => Color.FromRgb(90, 0, 130), // purple
                "missing" => Color.FromRgb(200, 160, 0), // yellow
                _ => Colors.Gray,
            };

            return animal switch
            {
                "dogs" => CreateZigzagBrush(baseColor),
                "cats" => CreateStripeBrush(baseColor),
                "kittens" => new SolidColorBrush(baseColor),
                _ => new SolidColorBrush(baseColor),
            };
        }

        private void DrawCircles()
        {
            if (MyMapView.GraphicsOverlays.Count == 0)
                return;

            var graphicsOverlay = MyMapView.GraphicsOverlays[0];
            graphicsOverlay.Graphics.Clear();

            bool showDogs = DogsToggle.IsChecked == true;
            bool showCats = CatsToggle.IsChecked == true;
            bool showKittens = KittensToggle.IsChecked == true;

            foreach (var shelter in _shelters)
            {
                if (shelter.Latitude == 0 && shelter.Longitude == 0)
                    continue;

                var location = new MapPoint(
                    shelter.Longitude,
                    shelter.Latitude,
                    SpatialReferences.Wgs84
                );

                // Calculate filtered totals
                int total = 0;
                int saved = 0;
                if (showDogs)
                {
                    total += shelter.Dogs;
                    saved += shelter.DogsSaved;
                }
                if (showCats)
                {
                    total += shelter.Cats;
                    saved += shelter.CatsSaved;
                }
                if (showKittens)
                {
                    total += shelter.Kittens;
                    saved += shelter.KittensSaved;
                }

                if (total == 0)
                    continue;

                double outerRadius = 10 + (total / 20.0);
                double innerRadius = outerRadius * ((double)saved / total);

                // Outer circle - total animals (red, semi transparent)
                var outerSymbol = new SimpleMarkerSymbol(
                    SimpleMarkerSymbolStyle.Circle,
                    System.Drawing.Color.FromArgb(255, 220, 80, 80),
                    outerRadius
                );

                // Inner circle - saved animals (green, full opacity, drawn on top)
                var innerSymbol = new SimpleMarkerSymbol(
                    SimpleMarkerSymbolStyle.Circle,
                    System.Drawing.Color.FromArgb(255, 50, 205, 50),
                    innerRadius
                );

                var outerGraphic = new Graphic(location, outerSymbol);
                outerGraphic.ZIndex = 0;

                var innerGraphic = new Graphic(location, innerSymbol);
                innerGraphic.ZIndex = 1;

                // Calculate save rate for selected animals
                double filteredSaveRate =
                    total > 0 ? Math.Round((double)saved / total * 100, 1) : 0;

                // Save rate label
                var textSymbol = new TextSymbol(
                    $"Save Rate {filteredSaveRate}%",
                    System.Drawing.Color.Black,
                    12,
                    Esri.ArcGISRuntime.Symbology.HorizontalAlignment.Left,
                    Esri.ArcGISRuntime.Symbology.VerticalAlignment.Middle
                );
                textSymbol.FontWeight = Esri.ArcGISRuntime.Symbology.FontWeight.Bold;

                // Offset the label slightly to the right of the circle
                var labelPoint = new MapPoint(
                    shelter.Longitude + 0.05,
                    shelter.Latitude,
                    SpatialReferences.Wgs84
                );

                var labelGraphic = new Graphic(labelPoint, textSymbol);
                graphicsOverlay.Graphics.Add(labelGraphic);

                outerGraphic.Attributes["name"] = shelter.Name;
                outerGraphic.Attributes["address"] = shelter.Address;
                outerGraphic.Attributes["dogs"] = shelter.Dogs;
                outerGraphic.Attributes["cats"] = shelter.Cats;
                outerGraphic.Attributes["kittens"] = shelter.Kittens;
                outerGraphic.Attributes["total"] = total;
                outerGraphic.Attributes["saveRate"] = shelter.SaveRate;
                outerGraphic.Attributes["NonLiveRate"] = shelter.NonLiveRate;

                graphicsOverlay.Graphics.Add(outerGraphic);
                graphicsOverlay.Graphics.Add(innerGraphic);
            }
        }

        private Point MapPointToScreen(MapPoint mapPoint)
        {
            var screenPoint = MyMapView.LocationToScreen(mapPoint);
            return new Point(screenPoint.X, screenPoint.Y);
        }

        private void DrawPieChartWithLabels(
            Point center,
            double radius,
            List<(double value, System.Windows.Media.Brush color)> segments
        )
        {
            if (segments.Sum(s => s.value) == 0)
                return;

            var nonZero = segments.Where(s => s.value > 0).ToList();

            // If only one segment, draw a full circle

            // problem when there was only one segment:

            // When you draw an arc in WPF, you define a start point and an end point on the circle's edge.
            // If there's only one segment, it takes up 100% of the pie —  meaning the start point and end point are the same point.

            //  WPF's ArcSegment can't draw an arc from a point back to itself,
            //  so it just draws a straight line between the same point, giving you the line you were seeing.

            // Why a full circle needs different treatment:
            // This is actually a well known limitation of arc - based drawing systems — not just WPF.
            //  SVG has the same problem. A full circle can't be represented as a single arc, so you have to handle it as a special case entirely.

            if (nonZero.Count == 1)
            {
                // Instead of trying to draw a 360 degree arc (which fails), it switches to a completely different WPF shape — Ellipse.
                // An Ellipse knows how to draw itself as a full circle natively without needing arc math.
                var ellipse = new System.Windows.Shapes.Ellipse
                {
                    Width = radius * 2,
                    Height = radius * 2,
                    Fill = nonZero[0].color,
                    Stroke = System.Windows.Media.Brushes.Black,
                    StrokeThickness = 1.5,
                };

                // The Canvas.SetLeft and Canvas.SetTop lines position the ellipse correctly.
                // This is important because WPF positions shapes by their top-left corner, not their center —
                // so we subtract the radius from both X and Y to shift it so the center of the ellipse lands exactly where we want it.
                Canvas.SetLeft(ellipse, center.X - radius);
                Canvas.SetTop(ellipse, center.Y - radius);
                PieChartCanvas.Children.Add(ellipse);

                // Then return exits the method early so the regular arc drawing code below never runs.

                // Label for single segment
                if (nonZero[0].value >= 1)
                    DrawSliceLabel(
                        center,
                        radius,
                        -Math.PI / 2,
                        Math.PI * 2,
                        (int)nonZero[0].value,
                        0
                    );

                return;

                // The key insight:
                // The fix doesn't try to make the arc work for a full circle —
                // it recognizes that a full circle is fundamentally a different shape that needs a different drawing approach,
                // and switches tools accordingly. That's a common pattern in graphics programming —
                // recognize the edge case and handle it separately rather than trying to force one approach to handle everything.
            }

            double total = segments.Sum(s => s.value);
            double startAngle = -Math.PI / 2;

            // sliceIndex tracks drawn segments (excludes zero-valued segments), this way the staggered labels alternate up/down correctly even when some segments are zero and skipped
            int sliceIndex = 0;

            foreach (var (value, color) in segments)
            {
                if (value == 0)
                    continue;

                double sweepAngle = (value / total) * 2 * Math.PI;
                double midAngle = startAngle + sweepAngle / 2;

                var path = new System.Windows.Shapes.Path();
                var figure = new PathFigure();
                figure.StartPoint = center;

                var startX = center.X + radius * Math.Cos(startAngle);
                var startY = center.Y + radius * Math.Sin(startAngle);
                var endAngle = startAngle + sweepAngle;
                var endX = center.X + radius * Math.Cos(endAngle);
                var endY = center.Y + radius * Math.Sin(endAngle);

                figure.Segments.Add(
                    new System.Windows.Media.LineSegment(new Point(startX, startY), true)
                );
                figure.Segments.Add(
                    new ArcSegment(
                        new Point(endX, endY),
                        new System.Windows.Size(radius, radius),
                        0,
                        sweepAngle > Math.PI,
                        SweepDirection.Clockwise,
                        true
                    )
                );
                figure.Segments.Add(new System.Windows.Media.LineSegment(center, true));
                figure.IsClosed = true;

                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);
                path.Data = geometry;
                path.Fill = color;
                path.Stroke = System.Windows.Media.Brushes.Black;
                path.StrokeThickness = 1.5;

                PieChartCanvas.Children.Add(path);

                if (value >= 1)
                    DrawSliceLabel(center, radius, startAngle, sweepAngle, (int)value, sliceIndex);

                // Only increment index for segments that are actually drawn
                sliceIndex++;

                startAngle = endAngle;
            }
        }

        private void DrawSliceLabel(
            Point center,
            double radius,
            double startAngle,
            double sweepAngle,
            int value,
            int sliceIndex
        )
        {
            double midAngle = startAngle + sweepAngle / 2;
            double leaderLength = 15;
            double labelOffset = 22;

            // Leader line start (edge of circle)
            var lineStart = new Point(
                center.X + radius * Math.Cos(midAngle),
                center.Y + radius * Math.Sin(midAngle)
            );

            // Leader line end
            var lineEnd = new Point(
                center.X + (radius + leaderLength) * Math.Cos(midAngle),
                center.Y + (radius + leaderLength) * Math.Sin(midAngle)
            );

            // Draw leader line
            var line = new System.Windows.Shapes.Line
            {
                X1 = lineStart.X,
                Y1 = lineStart.Y,
                X2 = lineEnd.X,
                Y2 = lineEnd.Y,
                Stroke = System.Windows.Media.Brushes.Black,
                StrokeThickness = 2,
            };
            PieChartCanvas.Children.Add(line);

            // ensure leader line is on top
            Panel.SetZIndex(line, 10000);

            // stagger logic

            double staggerAmount = 10;

            // Alternate up/down
            double yOffset = (sliceIndex % 2 == 0) ? -staggerAmount : staggerAmount;

            // Label position
            var labelPos = new Point(
                center.X + (radius + labelOffset) * Math.Cos(midAngle),
                center.Y + (radius + labelOffset) * Math.Sin(midAngle) + yOffset
            );

            var text = new TextBlock
            // TextBlock does not have a border property, so we have to wrap it in a Border
            {
                Text = $"{value}",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
            };

            var border = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 1, 3, 1),
                Child = text,
            };
            // Measure the border (NOT the text)
            border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = border.DesiredSize;

            // Center the border at the label position
            Canvas.SetLeft(border, labelPos.X - size.Width / 2);
            Canvas.SetTop(border, labelPos.Y - size.Height / 2);

            PieChartCanvas.Children.Add(border);

            // ensure label is on top of any pie charts
            Panel.SetZIndex(border, 10000);
        }

        private void RedrawPieCharts()
        {
            PieChartCanvas.Children.Clear();

            if (_shelters == null || MyMapView.GraphicsOverlays.Count == 0)
                return;

            bool isLive = ModeLive.IsChecked == true;
            bool isNonLive = ModeNonLive.IsChecked == true;

            if (!isLive && !isNonLive)
                return;

            foreach (var shelter in _shelters)
            {
                if (shelter.Latitude == 0 && shelter.Longitude == 0)
                    continue;

                var mapPoint = new MapPoint(
                    shelter.Longitude,
                    shelter.Latitude,
                    SpatialReferences.Wgs84
                );
                var screen = MapPointToScreen(mapPoint);

                if (
                    screen.X < 0
                    || screen.Y < 0
                    || screen.X > MyMapView.ActualWidth
                    || screen.Y > MyMapView.ActualHeight
                )
                    continue;

                double radius = 20 + (shelter.Total / 25.0);

                List<(double, System.Windows.Media.Brush)> segments;

                if (isLive)
                {
                    var (adoptions, bestFriends, newHope, redeemed, released) = GetFilteredLive(
                        shelter
                    );

                    segments = new List<(double, Brush)>
                    {
                        (adoptions, Brushes.MediumSeaGreen),
                        (bestFriends, Brushes.DodgerBlue),
                        (newHope, Brushes.MediumPurple),
                        (redeemed, Brushes.Orange),
                        (released, Brushes.SteelBlue),
                    };

                    DrawPieChartWithLabels(screen, radius, segments);
                    continue;
                }
                else
                {
                    var data = GetFilteredNonLive(shelter);

                    var labeledSegments = new List<(double value, System.Windows.Media.Brush color)>
                    {
                        (data.Euthanasia.Dogs, GetNonLiveColor("dogs", "euth")),
                        (data.Euthanasia.Cats, GetNonLiveColor("cats", "euth")),
                        (data.Euthanasia.Kittens, GetNonLiveColor("kittens", "euth")),
                        (data.Died.Dogs, GetNonLiveColor("dogs", "died")),
                        (data.Died.Cats, GetNonLiveColor("cats", "died")),
                        (data.Died.Kittens, GetNonLiveColor("kittens", "died")),
                        (data.Missing.Dogs, GetNonLiveColor("dogs", "missing")),
                        (data.Missing.Cats, GetNonLiveColor("cats", "missing")),
                        (data.Missing.Kittens, GetNonLiveColor("kittens", "missing")),
                    };

                    DrawPieChartWithLabels(screen, radius, labeledSegments);
                    continue;
                }
            }
        }

        private void HideAllSidebarPanels()
        {
            SaveRatePanel.Visibility = Visibility.Collapsed;
            LiveOutcomesPanel.Visibility = Visibility.Collapsed;
            NonLiveOutcomesPanel.Visibility = Visibility.Collapsed;
            AnimalCountsPanel.Visibility = Visibility.Collapsed;
            FosterPanel.Visibility = Visibility.Collapsed;
            OutcomeFilterPanel.Visibility = Visibility.Collapsed;
            LiveOutcomeFilterPanel.Visibility = Visibility.Collapsed;
        }

        private (bool dogs, bool cats, bool kittens) GetAnimalFilter()
        {
            return (
                DogsToggle.IsChecked == true,
                CatsToggle.IsChecked == true,
                KittensToggle.IsChecked == true
            );
        }

        private (bool euthanasia, bool died, bool missing) GetOutcomeFilter()
        {
            return (
                EuthanasiaToggle.IsChecked == true,
                DiedToggle.IsChecked == true,
                MissingToggle.IsChecked == true
            );
        }

        private (
            bool adoptions,
            bool bestFriends,
            bool newHope,
            bool redeemed,
            bool released
        ) GetLiveOutcomeFilter()
        {
            return (
                AdoptionsToggle.IsChecked == true,
                BestFriendsToggle.IsChecked == true,
                NewHopeToggle.IsChecked == true,
                RedeemedToggle.IsChecked == true,
                ReleasedToggle.IsChecked == true
            );
        }

        private void LiveOutcomeFilter_Changed(object sender, RoutedEventArgs e)
        {
            // redraw live pies and update legend/sidebar
            RedrawPieCharts();
            UpdateLegend();
            if (_selectedShelter != null)
                RefreshSidebar(_selectedShelter);
        }

        private NonLiveBreakdown GetFilteredNonLive(Shelter s)
        {
            var (dogs, cats, kittens) = GetAnimalFilter();
            var (euth, died, missing) = GetOutcomeFilter();

            var result = new NonLiveBreakdown();

            if (dogs)
            {
                if (euth)
                    result.Euthanasia.Dogs += s.NonLiveOutcomes.Dogs.Euthanasia;
                if (died)
                    result.Died.Dogs += s.NonLiveOutcomes.Dogs.DiedInCare;
                if (missing)
                    result.Missing.Dogs += s.NonLiveOutcomes.Dogs.Missing;
            }

            if (cats)
            {
                if (euth)
                    result.Euthanasia.Cats += s.NonLiveOutcomes.Cats.Euthanasia;
                if (died)
                    result.Died.Cats += s.NonLiveOutcomes.Cats.DiedInCare;
                if (missing)
                    result.Missing.Cats += s.NonLiveOutcomes.Cats.Missing;
            }

            if (kittens)
            {
                if (euth)
                    result.Euthanasia.Kittens += s.NonLiveOutcomes.Kittens.Euthanasia;
                if (died)
                    result.Died.Kittens += s.NonLiveOutcomes.Kittens.DiedInCare;
                if (missing)
                    result.Missing.Kittens += s.NonLiveOutcomes.Kittens.Missing;
            }

            return result;
        }

        private void OutcomeFilter_Changed(object sender, RoutedEventArgs e)
        {
            // redraw non-live pies and update legend/sidebar
            // will be called in MainWindow.xaml when any of the euthanasia/died/missing toggles are changed
            RedrawPieCharts();
            UpdateLegend();
            if (_selectedShelter != null)
                RefreshSidebar(_selectedShelter);
        }

        private (int adopt, int best, int hope, int redeem, int release) GetFilteredLive(Shelter s)
        {
            var (dogs, cats, kittens) = GetAnimalFilter();
            var (adoptOn, bestOn, hopeOn, redeemOn, releaseOn) = GetLiveOutcomeFilter();

            int adopt = 0,
                best = 0,
                hope = 0,
                redeem = 0,
                release = 0;

            if (dogs)
            {
                if (adoptOn)
                    adopt += s.LiveOutcomes.Dogs.Adoptions;
                if (bestOn)
                    best += s.LiveOutcomes.Dogs.BestFriends;
                if (hopeOn)
                    hope += s.LiveOutcomes.Dogs.NewHope;
                if (redeemOn)
                    redeem += s.LiveOutcomes.Dogs.Redeemed;
                if (releaseOn)
                    release += s.LiveOutcomes.Dogs.Released;
            }

            if (cats)
            {
                if (adoptOn)
                    adopt += s.LiveOutcomes.Cats.Adoptions;
                if (bestOn)
                    best += s.LiveOutcomes.Cats.BestFriends;
                if (hopeOn)
                    hope += s.LiveOutcomes.Cats.NewHope;
                if (redeemOn)
                    redeem += s.LiveOutcomes.Cats.Redeemed;
                if (releaseOn)
                    release += s.LiveOutcomes.Cats.Released;
            }

            if (kittens)
            {
                if (adoptOn)
                    adopt += s.LiveOutcomes.Kittens.Adoptions;
                if (bestOn)
                    best += s.LiveOutcomes.Kittens.BestFriends;
                if (hopeOn)
                    hope += s.LiveOutcomes.Kittens.NewHope;
                if (redeemOn)
                    redeem += s.LiveOutcomes.Kittens.Redeemed;
                if (releaseOn)
                    release += s.LiveOutcomes.Kittens.Released;
            }

            return (adopt, best, hope, redeem, release);
        }

        private int GetFilteredIntakeTotal(Shelter s)
        {
            var (dogs, cats, kittens) = GetAnimalFilter();

            int total = 0;

            if (dogs)
                total += s.Dogs;

            if (cats)
                total += s.Cats;

            if (kittens)
                total += s.Kittens;

            return total;
        }

        public static string GetPercentage(int value, int total, int decimals = 2)
        {
            if (total <= 0)
                // edge case, so we don't divide over 0
                return "0%";

            double percent = (double)value / total * 100;
            return $"{Math.Round(percent, decimals)}%";
            // decided against percentage.ToString("P") because
            // 1. don't need localization of numbers
            // 2. can better control the rounding behavior
        }

        private void RefreshSidebar(Shelter shelter)
        {
            HideAllSidebarPanels();

            // Always keep the animal filter visible when updating the sidebar
            AnimalFilterPanel.Visibility = Visibility.Visible;

            // Ensure the outcome filter stays visible when Non-Live mode is active
            LiveOutcomeFilterPanel.Visibility =
                ModeLive.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            OutcomeFilterPanel.Visibility =
                ModeNonLive.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            // Show basic identity info for the selected shelter regardless of current mode
            ShelterName.Text = shelter.Name;
            ShelterAddress.Text = shelter.Address;

            if (ModeIntake.IsChecked == true)
            {
                AnimalCountsPanel.Visibility = Visibility.Visible;

                DogCount.Text = $"🐕 Dogs: {shelter.Dogs}";
                CatCount.Text = $"🐈 Cats: {shelter.Cats}";
                KittenCount.Text = $"🐱 Kittens: {shelter.Kittens}";
                TotalCount.Text = $"Total: {shelter.Total}";

                SaveRatePanel.Visibility = Visibility.Visible;

                // var (dogs, cats, kittens) = GetAnimalFilter();

                //SaveRate.Text = $"Save Rate (filtered): {filteredSaveRate}%";

                DogSaveRate.Text =
                    shelter.Dogs > 0
                        ? $"🐕 Dog Save Rate: {Math.Round((double)shelter.DogsSaved / shelter.Dogs * 100, 1)}%"
                        : "";
                CatSaveRate.Text =
                    shelter.Cats > 0
                        ? $"🐈 Cat Save Rate: {Math.Round((double)shelter.CatsSaved / shelter.Cats * 100, 1)}%"
                        : "";
                KittenSaveRate.Text =
                    shelter.Kittens > 0
                        ? $"🐱 Kitten Save Rate: {Math.Round((double)shelter.KittensSaved / shelter.Kittens * 100, 1)}%"
                        : "";
            }
            else if (ModeLive.IsChecked == true)
            {
                LiveOutcomesPanel.Visibility = Visibility.Visible;

                var (adoptions, bestFriends, newHope, redeemed, released) = GetFilteredLive(
                    shelter
                );

                var totalLive = adoptions + bestFriends + newHope + redeemed + released;

                var totalIntake = GetFilteredIntakeTotal(shelter);

                Adoptions.Text = $"Adoptions: {adoptions},  {GetPercentage(adoptions, totalLive)}";
                BestFriends.Text =
                    $"Best Friends: {bestFriends}, {GetPercentage(bestFriends, totalLive)}";
                NewHope.Text = $"New Hope: {newHope}, {GetPercentage(newHope, totalLive)}";
                Redeemed.Text = $"Redeemed: {redeemed}, {GetPercentage(redeemed, totalLive)}";
                Released.Text = $"Released: {released}, {GetPercentage(released, totalLive)}";

                LiveRate.Text = $"Total Live: {totalLive}";
                SaveRate.Text = $"Save Rate: {GetPercentage(totalLive, totalIntake)}";
            }
            else if (ModeNonLive.IsChecked == true)
            {
                // Outcome filter already set above; show non-live panel
                NonLiveOutcomesPanel.Visibility = Visibility.Visible;

                var data = GetFilteredNonLive(shelter);

                var euthanasia = data.Euthanasia.Total;
                var died = data.Died.Total;
                var missing = data.Missing.Total;

                var totalIntake = GetFilteredIntakeTotal(shelter);

                var notSavedTotal = euthanasia + died + missing;

                var (dogs, cats, kittens) = GetAnimalFilter();

                var selectedAnimals = new List<string>();

                if (dogs)
                    selectedAnimals.Add("Dogs");
                if (cats)
                    selectedAnimals.Add("Cats");
                if (kittens)
                    selectedAnimals.Add("Kittens");

                NonLiveOutcomesChoices.Text =
                    selectedAnimals.Count > 0
                        ? $"Non Live Outcomes for:\n{string.Join(", ", selectedAnimals)}"
                        : "Non Live Outcomes (No animals selected)";
                Euthanasia.Text = $"Euthanasia: {euthanasia}";

                Died.Text = $"Died in Care: {died}";
                MissingAnimals.Text = $"Missing: {missing}";

                NotSavedTotal.Text = $"Total Non-Live: {euthanasia + died + missing}";
                NotSavedRate.Text = $"Not Saved Rate: {GetPercentage(notSavedTotal, totalIntake)}";
            }

            // fosters

            //DogFostered.Text = "🐕 Dogs Fostered";
            //CatFostered.Text = "🐈 Cats Fostered";
            //KittenFostered.Text = "🐱 Kittens Fostered";
        }

        private void MyMapView_GeoViewTapped(
            object sender,
            Esri.ArcGISRuntime.UI.Controls.GeoViewInputEventArgs e
        )
        {
            if (MyMapView.GraphicsOverlays.Count == 0)
                return;

            var tapLocation = GeometryEngine.Project(e.Location, SpatialReferences.Wgs84);

            var result = MyMapView
                .GraphicsOverlays[0]
                .Graphics.FirstOrDefault(g =>
                    g.IsVisible
                    &&
                    // BUG: System.Collections.Generic.KeyNotFoundException: 'The given key 'name' was not present in the dictionary.'
                    // The tap handler is hitting the label graphic or inner circle which don't have attributes. Fix it by checking if the name attribute exists before reading it
                    // Adding g.Attributes.ContainsKey("name") ensures it only matches the outer graphic which has all the shelter data!
                    g.Attributes.ContainsKey("name")
                    && GeometryEngine.Distance(g.Geometry, tapLocation) < 0.1
                );

            if (result == null)
                return;

            // Find the matching shelter object for full data
            var shelter = _shelters.FirstOrDefault(s =>
                s.Name == result.Attributes["name"]?.ToString()
            );
            if (shelter == null)
                return;

            _selectedShelter = shelter;
            RefreshSidebar(shelter);
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (MyMapView.GraphicsOverlays.Count == 0)
                return;

            HideAllSidebarPanels();

            // ALWAYS SHOW ANIMAL FILTER
            AnimalFilterPanel.Visibility = Visibility.Visible;
            bool isIntake = ModeIntake.IsChecked == true;
            bool isLive = ModeLive.IsChecked == true;
            bool isNonLive = ModeNonLive.IsChecked == true;

            // Show appropriate filter panel
            LiveOutcomeFilterPanel.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;
            OutcomeFilterPanel.Visibility = isNonLive ? Visibility.Visible : Visibility.Collapsed;

            if (isIntake)
            {
                MyMapView.GraphicsOverlays[0].IsVisible = true;
                PieChartCanvas.Children.Clear();
            }
            else
            // both isLive and isNonLive are pie charts
            {
                MyMapView.GraphicsOverlays[0].IsVisible = false;
                RedrawPieCharts();
            }

            UpdateLegend();

            // Apply filters immediately when switching mode
            if (isLive)
                LiveOutcomeFilter_Changed(this, new RoutedEventArgs());
            if (isNonLive)
                OutcomeFilter_Changed(this, new RoutedEventArgs());

            if (_selectedShelter != null)
                RefreshSidebar(_selectedShelter);
        }

        private void AnimalToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (MyMapView.GraphicsOverlays.Count > 0)
                DrawCircles();

            RedrawPieCharts();

            if (_selectedShelter != null)
                RefreshSidebar(_selectedShelter);
        }

        private void AddLegendSubItem(string label, Brush color)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(20, 2, 0, 2), // indent
            };

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = 12,
                Height = 12,
                Fill = color,

                Margin = new Thickness(0, 0, 6, 0),
            };

            var text = new TextBlock
            {
                Text = label,
                FontSize = 11,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };

            panel.Children.Add(rect);
            panel.Children.Add(text);
            LegendPanel.Children.Add(panel);
        }

        private void UpdateLegend()
        {
            LegendPanel.Children.Clear();

            if (ModeIntake.IsChecked == true)
            {
                LegendHeader.Text = "Legend";
                AddLegendItem("Total Intake", System.Windows.Media.Brushes.Red);
                AddLegendItem("Animals Saved", System.Windows.Media.Brushes.LimeGreen);
            }
            else if (ModeLive.IsChecked == true)
            {
                LegendHeader.Text = "Live Outcomes";
                AddLegendItem("Adoptions", System.Windows.Media.Brushes.MediumSeaGreen);
                AddLegendItem("Best Friends", System.Windows.Media.Brushes.DodgerBlue);
                AddLegendItem("New Hope", System.Windows.Media.Brushes.MediumPurple);
                AddLegendItem("Redeemed", System.Windows.Media.Brushes.Orange);
                AddLegendItem("Released", System.Windows.Media.Brushes.SteelBlue);
            }
            else if (ModeNonLive.IsChecked == true)
            {
                LegendHeader.Text = "Non-Live Outcomes";
                LegendPanel.Children.Clear();

                // Use the same base colors as GetNonLiveColor's Color.FromRgb(...) values

                // Euthanasia
                AddLegendItem("Euthanasia", new SolidColorBrush(Color.FromRgb(0, 90, 180)));
                AddLegendSubItem("Dogs", GetNonLiveColor("dogs", "euth"));
                AddLegendSubItem("Cats", GetNonLiveColor("cats", "euth"));
                AddLegendSubItem("Kittens", GetNonLiveColor("kittens", "euth"));

                // Died
                AddLegendItem("Died in Care", new SolidColorBrush(Color.FromRgb(90, 0, 130)));
                AddLegendSubItem("Dogs", GetNonLiveColor("dogs", "died"));
                AddLegendSubItem("Cats", GetNonLiveColor("cats", "died"));
                AddLegendSubItem("Kittens", GetNonLiveColor("kittens", "died"));

                // Missing
                AddLegendItem("Missing", new SolidColorBrush(Color.FromRgb(200, 160, 0)));
                AddLegendSubItem("Dogs", GetNonLiveColor("dogs", "missing"));
                AddLegendSubItem("Cats", GetNonLiveColor("cats", "missing"));
                AddLegendSubItem("Kittens", GetNonLiveColor("kittens", "missing"));
            }
        }

        private void AddLegendItem(string label, System.Windows.Media.Brush color)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2),
            };

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = 16,
                Height = 16,
                Fill = color,
                Margin = new Thickness(0, 0, 8, 0),
            };

            var text = new TextBlock
            {
                Text = label,
                FontSize = 12,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };

            panel.Children.Add(rect);
            panel.Children.Add(text);
            LegendPanel.Children.Add(panel);
        }
    }
}
