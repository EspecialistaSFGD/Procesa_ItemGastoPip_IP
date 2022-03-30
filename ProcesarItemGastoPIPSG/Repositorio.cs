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

        public Repositorio(string conexion)
        {
            Conexion = conexion;
            proxyManager = ProxyManager.GetNewProxyManager();
            typeConvertionsManager = TypeConvertionManager.GetNewTypeConvertionManager();
            TiempoEsperaCargadoMasivo = 10000;
            BatchSize = 50000;
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
                var invocacionesErradas = new List<string>();
                var numeroReintento = 0;
                var request = new ProxyManager.Request();

                Console.WriteLine($"Consulta de servicio : { ejecutora.UrlWebService }.");
                request.HttpMethod = ProxyManager.HttpMethod.Get;
                request.Uri = ejecutora.UrlWebService;
                request.MediaType = ProxyManager.MediaType.Xml;
                var respuesta = new ProxyManager.Response { Ok = false };
                while (!respuesta.Ok && (numeroReintento <= numeroReintentosMaximo))
                {
                    if (numeroReintento > 0)
                    {
                        Console.WriteLine($"Se procede con el reintento numero : {numeroReintento} de consulta al servicio");
                    }
                    try
                    {
                        respuesta = await proxyManager.CallServiceAsync(request);
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine($"Error al intentar comunicarse con el servicio del MEF. Detalle del error => {exception.Message}");
                        respuesta.Ok = false;
                    }
                    numeroReintento++;
                }

                if (respuesta.Ok)
                {
                    var listadoItems = typeConvertionsManager.XmlStringToObject<RespuestaServicio>(respuesta.ResponseBody, "DataGasto");
                    itemsRespuesta = listadoItems.Items;
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
