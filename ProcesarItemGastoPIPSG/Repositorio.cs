using Dapper;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;
using NextSIT.Utility;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ProcesarItemGastoPIPSG
{
    public class Repositorio
    {
        private readonly string Conexion = "";
        private readonly ProxyManager proxyManager;
        private readonly TypeConvertionManager typeConvertionsManager;
        private readonly int TiempoEsperaCargadoMasivo;
        private readonly int BatchSize;
        private readonly string SOAP_ACTION;

        public Repositorio(string conexion)
        {
            Conexion = conexion;
            proxyManager = ProxyManager.GetNewProxyManager();
            typeConvertionsManager = TypeConvertionManager.GetNewTypeConvertionManager();
            TiempoEsperaCargadoMasivo = 10000;
            BatchSize = 50000;
            SOAP_ACTION = "SOAPAction";
        }

        //Paso 1.- Obtener el listado de Ejecutoras
        public async Task<List<WebServiceEjecutar>> ObtenerListadoInvocaciones()
        {
            using var conexionSql = new SqlConnection(Conexion);
            conexionSql.Open();
            try
            {
                var respuesta = await conexionSql.QueryAsync<WebServiceEjecutar>("dbo.02A_GenerarListadoEjecutora", commandType: CommandType.StoredProcedure, commandTimeout: 1200);
                return respuesta.ToList();
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                throw;
            }
            finally
            {
                conexionSql.Close();
            }
        }

        //Paso 2.- Recuperar los datos de la ejecutora
        public async Task<List<Item>> ObtenerItemsPorEjecutora(WebServiceEjecutar ejecutora, int numeroReintentosMaximo)
        {
            var itemsRespuesta = new List<Item>();
            try
            {
                Console.WriteLine($"Consulta de servicio : {ejecutora.UrlWebService}.");
                var datosRequest = ejecutora.UrlWebService.Split('|');
                var cabeceras = new Dictionary<string, string>();
                cabeceras.Add(SOAP_ACTION, datosRequest[2]);

                var clientHandler = new HttpClientHandler();
                using (var client = new HttpClient(clientHandler))
                {
                    client.Timeout = TimeSpan.FromMinutes(120);
                    foreach (var item in cabeceras)
                    {
                        client.DefaultRequestHeaders.Add(item.Key, item.Value);
                    }

                    var response = await client.PostAsync(datosRequest[0], new StringContent(datosRequest[1], Encoding.UTF8, "text/xml"));

                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        var respuestaItems = typeConvertionsManager.XmlStringToObject<RespuestaServicio>(body, "soap:Envelope.soap:Body.ObtenerDataGastoPIPResponse.DataGasto");
                        Console.WriteLine($"Se han recuperado los itemas de gasto desde el servicio del MEF. Numero de items para el mes => {respuestaItems.Items.Count}");
                        itemsRespuesta = respuestaItems.Items;
                    }
                    else
                    {
                        throw new Exception($"Error en respuesta =>  {await response.Content.ReadAsStringAsync()}");
                    }

                }
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Ocurrio un problema al intentar recuperar la informacion del servicio.\nError Asociado: {exception.Message}");
                itemsRespuesta =  new List<Item>();
            }
            return itemsRespuesta;
        }

        //Paso 3.- Eliminar Item antiguos de la unidad ejecutora
        public async Task<bool> EliminarItemsPorEjecutora(WebServiceEjecutar ejecutora)
        {
            using var conexionSql = new SqlConnection(Conexion);
            try
            {
                conexionSql.Open();
                var parameters = new DynamicParameters();
                parameters.Add("@SecEjec", ejecutora.SecEjec, DbType.String, ParameterDirection.Input, 20);

                var respuesta = await conexionSql.QueryAsync<WebServiceEjecutar>("dbo.02B_EliminarItemGastoPipSgPorEjecutora", parameters, commandType: CommandType.StoredProcedure, commandTimeout: 1200);
                return true;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                throw;
            }
            finally
            {
                conexionSql.Close();
            }

        }

        //Paso 4.- Registrar los Item de la unidad ejecutora
        public bool RegistrarItemsPorLotes(DataTable valores)
        {
            using var conexionSql = new SqlConnection(Conexion);
            conexionSql.Open();

            using SqlBulkCopy bulkCopy = new(conexionSql);
            bulkCopy.BulkCopyTimeout = TiempoEsperaCargadoMasivo;
            bulkCopy.BatchSize = BatchSize;
            bulkCopy.DestinationTableName = "dbo.ItemGastoPIPSG";

            try
            {
                bulkCopy.WriteToServer(valores);
                return true;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                throw;

            }
            finally
            {
                conexionSql.Close();
            }
        }
        //Paso 5.- Actualizar la unidad ejecutora a estado procesado
        public async Task<bool> ActualizarEjecutora(WebServiceEjecutar ejecutora)
        {
            using var conexionSql = new SqlConnection(Conexion);
            try
            {
                conexionSql.Open();
                var parameters = new DynamicParameters();
                parameters.Add("@SecEjec", ejecutora.SecEjec, DbType.String, ParameterDirection.Input, 20);

                var respuesta = await conexionSql.QueryAsync<WebServiceEjecutar>("dbo.02C_ActualizarEjecutora", parameters, commandType: CommandType.StoredProcedure, commandTimeout: 1200);
                return true;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                throw;
            }
            finally
            {
                conexionSql.Close();
            }

        }

        //Paso 6.- Enviar mail por concepto de error o éxito
        public void SendMail(Mail configuracion, string asunto, string mensaje)
        {
            try
            {
                // create message
                var email = new MimeMessage();
                email.Sender = MailboxAddress.Parse(configuracion.De);
                string[] destinatarios = configuracion.Para.Split(";");

                foreach(string destinatario in destinatarios) email.To.Add(MailboxAddress.Parse(destinatario));
                email.Subject = asunto;//"Notificaciones Mapa Inversiones - Sincronizacion de Datos del MEF";
                email.Body = new TextPart(TextFormat.Html) { Text = mensaje };

                // send email
                using var smtp = new SmtpClient();
                smtp.Connect(configuracion.Servidor, configuracion.Puerto, SecureSocketOptions.StartTls);
                smtp.Authenticate(configuracion.De, configuracion.Clave);
                smtp.Send(email);
                smtp.Disconnect(true);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Ocurrio un problema al enviar la notificacion de la carga fallida. Detalle del error => { exception.Message }");
            }
        } 
    }
}
