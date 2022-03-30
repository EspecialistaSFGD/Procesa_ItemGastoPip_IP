using Microsoft.Extensions.Configuration;
using NextSIT.Utility;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;

namespace ProcesarItemGastoPIPSG
{
    class Program
    {
        private static IConfigurationRoot Configuration { get; set; }

        static void Main(string[] args)
        {
            var conexionBd = "";
            var numeroReintentos = "";
            var servidorSmtp = "";
            var puertoSmtp = "";
            var usuarioSmtp = "";
            var claveSmtp = "";
            var deSmtp = "";
            var paraSmtp = "";
            try
            {
                conexionBd = string.IsNullOrEmpty(args[0]) ? "" : args[0];
                numeroReintentos = string.IsNullOrEmpty(args[1]) ? "" : args[1];
                servidorSmtp = string.IsNullOrEmpty(args[2]) ? "" : args[2];
                puertoSmtp = string.IsNullOrEmpty(args[3]) ? "" : args[3];
                usuarioSmtp = string.IsNullOrEmpty(args[4]) ? "" : args[4];
                claveSmtp = string.IsNullOrEmpty(args[5]) ? "" : args[5];
                deSmtp = string.IsNullOrEmpty(args[6]) ? "" : args[6];
                paraSmtp = string.IsNullOrEmpty(args[7]) ? "" : args[7];

                Console.WriteLine("Parametros enviados desde la consola");
                Console.WriteLine(args == null ? "No hay parametros" : string.Join('-', args));
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Algunos parametros no han sido transferidos a la consola, se utilizaran los valores por defecto. Detalle del error => {exception.Message}");
            }

            IConfigurationBuilder builder = new ConfigurationBuilder()
               .AddJsonFile("appsettings.json")
               .AddEnvironmentVariables();

            Configuration = builder.Build();

            var mailConfiguration = new Mail
            {
                Servidor = !string.IsNullOrEmpty(servidorSmtp)? servidorSmtp : Configuration.GetSection("Servidor").Value.ToString(),
                Puerto = int.Parse(!string.IsNullOrEmpty(puertoSmtp) ? puertoSmtp :  Configuration.GetSection("Puerto").Value.ToString()),
                Usuario = !string.IsNullOrEmpty(usuarioSmtp) ? usuarioSmtp : Configuration.GetSection("Usuario").Value.ToString(),
                Clave = !string.IsNullOrEmpty(claveSmtp) ? claveSmtp : Configuration.GetSection("Clave").Value.ToString(),
                De = !string.IsNullOrEmpty(deSmtp) ? deSmtp : Configuration.GetSection("De").Value.ToString(),
                Para = !string.IsNullOrEmpty(paraSmtp) ? paraSmtp : Configuration.GetSection("Para").Value.ToString()
            };

            EjecutarProceso(
                    !string.IsNullOrEmpty(conexionBd) ? conexionBd : Configuration.GetConnectionString("ConexionPcm"), 
                    int.Parse(!string.IsNullOrEmpty(numeroReintentos) ? numeroReintentos : Configuration.GetSection("NumeroReintentosMaximo").Value.ToString()),
                    mailConfiguration
                    )
                .GetAwaiter()
                .GetResult();
        }


        static async Task EjecutarProceso(string conexion, int numeroReintentosMaximo, Mail mail)
        {
            try
            {
                Console.WriteLine($"--------------------------------------------------------------------");
                Console.WriteLine($"    Proceso de carga de Items de Gasto para el anio configurado");
                Console.WriteLine($"--------------------------------------------------------------------");
                var mensajeRespuesta = @"<h3>Proceso de carga de Items de Gasto</h3><p>mensaje_respuesta</p>";
                var proxyManager = ProxyManager.GetNewProxyManager();
                var typeConvertionsManager = TypeConvertionManager.GetNewTypeConvertionManager();
                var fileManager = FileManager.GetNewFileManager();
                var request = new ProxyManager.Request();
                var repositorio = new Repositorio(conexion);

                var listaWebService = await repositorio.ObtenerListadoInvocaciones();
                var listaErrados = new List<string>();
                Console.WriteLine($"Numero de invocaciones que tendra el servicio : {listaWebService.Count}");
                Console.WriteLine($"Se inicia el proceso de carga a la base de datos desde el servicio");
                Console.WriteLine($"Numero de reintentos maximos para consulta de servicio : {numeroReintentosMaximo}");

                foreach(var invocacion in listaWebService)
                {
                    //Recupera los items
                    var items = await repositorio.ObtenerItemsPorEjecutora(invocacion, numeroReintentosMaximo);
                    Console.WriteLine($"Items recuperados para la unidad ejecutora {invocacion.SecEjec} => {items.Count}");
                    //Elimina las existencias anteriores si hay registros nuevos
                    if(items.Count <= 0)
                    {
                        listaErrados.Add($"<tr><td>{invocacion.SecEjec}</td><td>La unidad ejecutora no posee registros para el año configurado</td></tr>");
                        continue;
                    }

                    var hanSidoEliminados = await repositorio.EliminarItemsPorEjecutora(invocacion);

                    if (!hanSidoEliminados)
                    {
                        listaErrados.Add($"<tr><td>{invocacion.SecEjec}</td><td>No se han podido eliminar la informacion previa de los items para la unidad ejecutora</td></tr>");
                        continue;
                    }

                    Console.WriteLine($"Existencias previas eliminadas para la unidad ejecutora {invocacion.SecEjec} del anio {invocacion.Anio}");
                    var origenCargaMasiva = typeConvertionsManager.ArrayListToDataTable(new ArrayList(items));
                    var hanSidoRegistrados = repositorio.RegistrarItemsPorLotes(origenCargaMasiva);

                    if (!hanSidoRegistrados)
                    {
                        listaErrados.Add($"<tr><td>{invocacion.SecEjec}</td><td>No se han podido registrar los items de la unidad ejecutora</td></tr>");
                        continue;
                    }
                    Console.WriteLine($"Items registrados correctamente para la unidad ejecutora {invocacion.SecEjec} del anio {invocacion.Anio}");
                    var haSidoActualizado = await repositorio.ActualizarEjecutora(invocacion);
                    if (!haSidoActualizado)
                    {
                        listaErrados.Add($"<tr><td>{invocacion.SecEjec}</td><td>No se ha podido actualizar el estado de la unidad ejecutora, se debe volver a procesar</td></tr>");
                        continue;
                    }

                }

                var detalle = listaErrados.Count == 0 ? 
                    "Los items de gasto de las unidades ejecutoras se han registrado correctamente" : 
                    listaErrados.Count == listaWebService.Count ? 
                    "No se han procesado los items de gasto, por favor revisar el proceso ETL configurado":
                    "Los items de gasto se han procesado parcialmente, sin embargo existen algunas observaciones:";

                var listadoDetalle = $"<table><thead><tr><th>Ejecutora</th><th>Mensaje de Error</th></tr></thead><tbody>{string.Join(' ', listaErrados)}</tbody></table>";

                mensajeRespuesta = mensajeRespuesta.Replace("mensaje_respuesta", detalle);
                mensajeRespuesta += listadoDetalle;

                repositorio.SendMail(mail, "Proceso de Carga Masiva de Datos de Proyectos", mensajeRespuesta);

                return;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);

                throw;
            }

        }
    }
}
