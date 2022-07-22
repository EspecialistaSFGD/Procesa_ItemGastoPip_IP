using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace ProcesarItemGastoPIPSG
{
    [JsonObject(Title = "DataGasto")]
    public class RespuestaServicio
    {
        /*[JsonProperty("@xmlns")]
        public string UriServicio { get; set; } = "http://www.mef.gob.pe/";*/
        [JsonProperty("Item")]
        [JsonConverter(typeof(SingleOrArrayConverter<Item>))]
        public List<Item> Items { get; set; } = new List<Item>();
    }

    public class Item
    {
        public int AnoEje { get; set; }
        public int MesEje { get; set; }
        public string IdNivelGobierno { get; set; }
        public string Sub_tipo_gobierno { get; set; }
        public string IdSector { get; set; }
        public string IdPliego { get; set; }
        public string IdEjecutora { get; set; }
        public string SecEjec { get; set; }
        public string Funcion { get; set; }
        public string Programa { get; set; }
        public string Programa_ppto { get; set; }
        public string Categ_gasto { get; set; }
        public string Tipo_transaccion { get; set; }
        public string Generica { get; set; }
        public string Subgenerica { get; set; }
        public string Subgenerica_det { get; set; }
        public string Especifica { get; set; }
        public string Especifica_det { get; set; }
        public string SecFunc { get; set; }
        public string IdFuente { get; set; }
        public string IdRubro { get; set; }
        public string IdTipoRecurso { get; set; }
        public string IdComponente { get; set; }
        public string IdProyecto { get; set; }
        public string TipoActProy { get; set; }
        public string IdMeta { get; set; }
        public string IdFinalidad { get; set; }
        public string IdProyectoSNIP { get; set; }
        public string Departamento { get; set; }
        public string Provincia { get; set; }
        public string Distrito { get; set; }
        public string Unidad_Medida { get; set; }
        public string PIA { get; set; }
        public string PIM { get; set; }
        public string Certificado { get; set; }
        public string CompAnual { get; set; }
        public string AtencionCompMensual { get; set; }
        public string Devengado { get; set; }
        public string Girado { get; set; }

        public string Multiejecutora { get; set; }
        public DateTime FechaInsercion { get; set; } = DateTime.Now;
    }

    public class SingleOrArrayConverter<T> : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(List<T>));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JToken token = JToken.Load(reader);
            if (token.Type == JTokenType.Array)
            {
                return token.ToObject<List<T>>();
            }
            return new List<T> { token.ToObject<T>() };
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
