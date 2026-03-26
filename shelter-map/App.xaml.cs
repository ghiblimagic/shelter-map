using Esri.ArcGISRuntime;
using System.Configuration;
using System.Data;
using System.Text.Json;
using System.Windows;
using System.IO;



namespace shelter_map
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            var json = File.ReadAllText("appsettings.json");
            var config = JsonDocument.Parse(json);
            var apiKey = config.RootElement.GetProperty("ArcGisApiKey").GetString();


            ArcGISRuntimeEnvironment.ApiKey = apiKey;
        }
    }

}
