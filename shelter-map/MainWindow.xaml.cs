using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI.Controls;
using Esri.ArcGISRuntime.Geometry;
using System.IO;
using System.Text.Json;
using Esri.ArcGISRuntime.Tasks.Geocoding;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;

namespace shelter_map
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            InitializeMap();
            _ = GeocodeAndPlotShelters();

        }

        private List<Shelter> _shelters = new List<Shelter>();

        private void InitializeMap()
        {
            var map = new Map(BasemapStyle.ArcGISStreets);
            MyMapView.Map = map;
            MyMapView.GeoViewTapped += MyMapView_GeoViewTapped;

            var losAngelesCounty = new Envelope(
                -118.9, 33.7,
                -117.6, 34.8,
                SpatialReferences.Wgs84);

            MyMapView.SetViewpoint(new Viewpoint(losAngelesCounty));
        }

        private List<Shelter> LoadShelters()
        {
            var json = File.ReadAllText("shelters.json");
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<List<Shelter>>(json, options);
        }

        private void DrawCircles()
        {
            if (MyMapView.GraphicsOverlays.Count == 0) return;

            var graphicsOverlay = MyMapView.GraphicsOverlays[0];
            graphicsOverlay.Graphics.Clear();

            bool showDogs = DogsToggle.IsChecked == true;
            bool showCats = CatsToggle.IsChecked == true;
            bool showKittens = KittensToggle.IsChecked == true;

            foreach (var shelter in _shelters)
            {
                if (shelter.Latitude == 0 && shelter.Longitude == 0) continue;

                var location = new MapPoint(
                    shelter.Longitude, shelter.Latitude, SpatialReferences.Wgs84);

                // Calculate filtered totals
                int total = 0;
                int saved = 0;
                if (showDogs) { total += shelter.Dogs; saved += shelter.DogsSaved; }
                if (showCats) { total += shelter.Cats; saved += shelter.CatsSaved; }
                if (showKittens) { total += shelter.Kittens; saved += shelter.KittensSaved; }

                if (total == 0) continue;

                double outerRadius = 10 + (total / 20.0);
                double innerRadius = outerRadius * ((double)saved / total);

                // Outer circle - total animals (red, semi transparent)
                var outerSymbol = new SimpleMarkerSymbol(
                    SimpleMarkerSymbolStyle.Circle,
                    System.Drawing.Color.FromArgb(255, 220, 80, 80),
                    outerRadius);

                // Inner circle - saved animals (green, full opacity, drawn on top)
                var innerSymbol = new SimpleMarkerSymbol(
                    SimpleMarkerSymbolStyle.Circle,
                    System.Drawing.Color.FromArgb(255, 50, 205, 50),
                    innerRadius);

                var outerGraphic = new Graphic(location, outerSymbol);
                outerGraphic.ZIndex = 0;

                var innerGraphic = new Graphic(location, innerSymbol);
                innerGraphic.ZIndex = 1;

                // Calculate save rate for selected animals
                double filteredSaveRate = total > 0 ? Math.Round((double)saved / total * 100, 1) : 0;

                // Save rate label
                var textSymbol = new TextSymbol(
                    $"{filteredSaveRate}%",
                    System.Drawing.Color.Black,
                    12,
                    Esri.ArcGISRuntime.Symbology.HorizontalAlignment.Left,
                    Esri.ArcGISRuntime.Symbology.VerticalAlignment.Middle);
                textSymbol.FontWeight = Esri.ArcGISRuntime.Symbology.FontWeight.Bold;

                // Offset the label slightly to the right of the circle
                var labelPoint = new MapPoint(
                    shelter.Longitude + 0.05,
                    shelter.Latitude,
                    SpatialReferences.Wgs84);

                var labelGraphic = new Graphic(labelPoint, textSymbol);
                graphicsOverlay.Graphics.Add(labelGraphic);

                outerGraphic.Attributes["name"] = shelter.Name;
                outerGraphic.Attributes["address"] = shelter.Address;
                outerGraphic.Attributes["dogs"] = shelter.Dogs;
                outerGraphic.Attributes["cats"] = shelter.Cats;
                outerGraphic.Attributes["kittens"] = shelter.Kittens;
                outerGraphic.Attributes["total"] = total;
                outerGraphic.Attributes["saveRate"] = shelter.SaveRate;

                graphicsOverlay.Graphics.Add(outerGraphic);
                graphicsOverlay.Graphics.Add(innerGraphic);
            }
        }

        private async Task GeocodeAndPlotShelters()
        {
            try
            {
                _shelters = LoadShelters();
                var locator = await LocatorTask.CreateAsync(
                    new Uri("https://geocode-api.arcgis.com/arcgis/rest/services/World/GeocodeServer"));

                var graphicsOverlay = new GraphicsOverlay();
                MyMapView.GraphicsOverlays.Add(graphicsOverlay);

                foreach (var shelter in _shelters)
                {
                    var geocodeParams = new GeocodeParameters { MaxResults = 1 };
                    var results = await locator.GeocodeAsync(shelter.Address, geocodeParams);
                    if (results.Count == 0) continue;

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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }


        private void MyMapView_GeoViewTapped(object sender, Esri.ArcGISRuntime.UI.Controls.GeoViewInputEventArgs e)
        {
            var tapLocation = GeometryEngine.Project(e.Location, SpatialReferences.Wgs84);

            var result = MyMapView.GraphicsOverlays[0]
                .Graphics
                .FirstOrDefault(g => g.IsVisible &&
                    // BUG: System.Collections.Generic.KeyNotFoundException: 'The given key 'name' was not present in the dictionary.'
                    // The tap handler is hitting the label graphic or inner circle which don't have attributes. Fix it by checking if the name attribute exists before reading it
                    // Adding g.Attributes.ContainsKey("name") ensures it only matches the outer graphic which has all the shelter data!
                    g.Attributes.ContainsKey("name") &&
                    GeometryEngine.Distance(g.Geometry, tapLocation) < 0.1);

            if (result == null) return;


            // Find the matching shelter object for full data
            var shelter = _shelters.FirstOrDefault(s => s.Name == result.Attributes["name"]?.ToString());
            if (shelter == null) return;

            bool showDogs = DogsToggle.IsChecked == true;
            bool showCats = CatsToggle.IsChecked == true;
            bool showKittens = KittensToggle.IsChecked == true;

            int total = 0;
            int saved = 0;
            if (showDogs) { total += shelter.Dogs; saved += shelter.DogsSaved; }
            if (showCats) { total += shelter.Cats; saved += shelter.CatsSaved; }
            if (showKittens) { total += shelter.Kittens; saved += shelter.KittensSaved; }

            double filteredSaveRate = total > 0 ? Math.Round((double)saved / total * 100, 1) : 0;

            ShelterName.Text = shelter.Name;
            ShelterAddress.Text = shelter.Address;
            DogCount.Text = showDogs ? $"🐕 Dogs: {shelter.Dogs} (saved: {shelter.DogsSaved})" : "";
            CatCount.Text = showCats ? $"🐈 Cats: {shelter.Cats} (saved: {shelter.CatsSaved})" : "";
            KittenCount.Text = showKittens ? $"🐱 Kittens: {shelter.Kittens} (saved: {shelter.KittensSaved})" : "";
            TotalCount.Text = $"Total: {total} (saved: {saved})";
            SaveRate.Text = $"Save Rate Total: {filteredSaveRate}%";
            DogSaveRate.Text = shelter.Dogs > 0 ?
                $"🐕 Dog Save Rate: {Math.Round((double)shelter.DogsSaved / shelter.Dogs * 100, 1)}%" : "";
            CatSaveRate.Text = shelter.Cats > 0 ?
                $"🐈 Cat Save Rate: {Math.Round((double)shelter.CatsSaved / shelter.Cats * 100, 1)}%" : "";
            KittenSaveRate.Text = shelter.Kittens > 0 ?
                $"🐱 Kitten Save Rate: {Math.Round((double)shelter.KittensSaved / shelter.Kittens * 100, 1)}%" : "";
        }

        private void AnimalToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (MyMapView.GraphicsOverlays.Count > 0)
                DrawCircles();
        }


    }
}