using HtmlAgilityPack;
using System.Text;
using static System.Environment;
using Microsoft.Extensions.Configuration;

namespace ImageDownloader
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Preguntar por url
            Console.Write("url: ");

            // URL de la imagen
            var url = Console.ReadLine();

            var ID = new ImageDownloader();

            ID.ConfigurarRutaGuardado();

            await ID.ConsultarURL(url);

            Console.WriteLine("");
            Console.WriteLine("Presiona cualquier tecla para cerrar.");
            Console.ReadKey();
        }        
    }
    public class ImageDownloader
    {
        public HttpClient HttpClient { get; set; } = new HttpClient();
        
        string html = string.Empty;
        HtmlDocument htmlDocument;
        string rutaDescargas = string.Empty;
        
        private readonly IConfiguration config;
        
        private readonly List<string> ListaCaracteresInvalidos;
        private readonly List<string> ListaExtensionesPermitidas;

        public ImageDownloader()
        {
            htmlDocument = new HtmlDocument();

            var builder = new ConfigurationBuilder();
            builder.SetBasePath(Directory.GetCurrentDirectory())
                   .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            config = builder.Build();
            
            ListaCaracteresInvalidos = config.GetSection("CaracteresInvalidos").Get<List<string>>();
            ListaExtensionesPermitidas = config.GetSection("ExtensionesPermitidas").Get<List<string>>();
        }

        public void ConfigurarRutaGuardado()
        {
            string rutaBase = GetFolderPath(Environment.SpecialFolder.UserProfile);
            rutaDescargas = Path.Combine(rutaBase, "Descargas");
            if (!Directory.Exists(rutaDescargas))
            {
                rutaDescargas = Path.Combine(rutaBase, "Downloads");
            }
        }

        public async Task ConsultarURL(string websiteUrl)
        {
            try
            {
                // Validacion de URL
                if (!Uri.TryCreate(websiteUrl, UriKind.Absolute, out Uri uri))
                {
                    Console.WriteLine("URL no válida.");
                    return;
                }

                // Obtener la pagina web completa de la revista
                html = await GetHtmlAsync(websiteUrl);

                // Validar si se cargo correctamente o no se obtuvo nada
                if (html == "")
                {
                    Console.WriteLine($"No se pudo acceder a: {websiteUrl}");
                    return;
                }

                // Crear nuevo HtmlDocument y pasarle como string toda la pagina que consultamos
                htmlDocument = new HtmlDocument();
                htmlDocument.LoadHtml(html);

                var titleNode = htmlDocument.DocumentNode.SelectSingleNode("//title");
                string titulo = HtmlEntity.DeEntitize(titleNode.InnerText);
                titulo = FormatearTitulo(titulo);

                string destinationFolder = Path.Combine(rutaDescargas, titulo);

                // Crear la carpeta de destino si no existe
                if (!Directory.Exists(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }

                Console.WriteLine("");
                Console.WriteLine($"Descargando {titulo}");

                await DownloadAllImagesAsync(destinationFolder);

                Console.WriteLine($"Descarga de {titulo} completada.");
                Console.WriteLine("");
            }
            catch (UriFormatException ex)
            {
                Console.WriteLine($"La URL no es válida: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ocurrió un error: {ex.Message} {ex.InnerException?.Message ?? ""}");
            }
        }        

        private async Task DownloadAllImagesAsync(string destinationFolder)
        {
            try
            {
                // Extraer las URL de las imágenes
                var imageUrls = GetImageUrls();

                // Definir el semáforo con el número máximo de conexiones concurrentes permitidas
                SemaphoreSlim semaphore = new SemaphoreSlim(10); // Ejemplo: Permitir hasta 5 conexiones simultáneas

                int i = 0;
                // Usar el semáforo para limitar el acceso concurrente al recurso compartido
                await Task.WhenAll(imageUrls.Select(async imageUrl =>
                {
                    await semaphore.WaitAsync(); // Esperar hasta que haya una ranura disponible en el semáforo
                    try
                    {
                        int currentTaskIndex = Interlocked.Increment(ref i);
                        var filename = $"{currentTaskIndex:D4}{Path.GetExtension(imageUrl)}";
                        var destinationPath = Path.Combine(destinationFolder, filename);

                        //Console.Write($"Descargando: {imageUrl} - ");

                        var isIsuccessful = await DownloadImageAsync(imageUrl, destinationPath);
                        //Console.WriteLine($"{(isIsuccessful ? "correcto" : "falló")}");
                    }
                    finally
                    {
                        semaphore.Release(); // Liberar el semáforo cuando se completa la tarea
                    }
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ocurrió un error: {ex.Message} {ex.InnerException?.Message ?? ""}");
                return;
            }
        }

        private List<string> GetImageUrls()
        {
            //var imageNodes = htmlDocument.DocumentNode.SelectNodes("//img");
            var imageNodes = htmlDocument.DocumentNode.SelectNodes("//img[not(contains(@class, 'PostPreview-coverImage')) and not(contains(@class, 'ob-RelatedPost-image'))]");

            var imageUrls = new List<string>();

            foreach (var imageNode in imageNodes)
            {
                var imageUrl = imageNode.Attributes["src"].Value;
                var ext = Path.GetExtension(imageUrl);

                if (!ListaExtensionesPermitidas.Contains(ext.ToLower()))
                    continue;

                imageUrls.Add(imageUrl);
            }

            return imageUrls;
        }

        public async Task<bool> DownloadImageAsync(string url, string destinationPath)
        {
            var isSuccessful = true;
            try
            {

                var response = await HttpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var imageBytes = await response.Content.ReadAsByteArrayAsync();

                await File.WriteAllBytesAsync(destinationPath, imageBytes);
            }
            catch (Exception)
            {
                isSuccessful = false;
            }
            return isSuccessful;
        }

        private string FormatearTitulo(string titulo)
        {
            StringBuilder nuevoTitulo = new StringBuilder();
            foreach (char caracter in titulo)
            {
                nuevoTitulo.Append(ListaCaracteresInvalidos.Contains(caracter.ToString()) ? " " : caracter);                
            }
            return nuevoTitulo.ToString();
        }

        private async Task<string> GetHtmlAsync(string url)
        {
            try
            {
                var response = await HttpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync();

                return html;
            }
            catch (Exception)
            {
                return "";
            }
        }             
    }
}