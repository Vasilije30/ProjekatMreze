using System;

namespace Domain
{
    [Serializable]
    public class OdgovorServera
    {
        public bool Odobren { get; set; }
        public string Poruka { get; set; }
        public int NovaX { get; set; } = -1;
        public int NovaY { get; set; } = -1;
        public int NovaZ { get; set; } = -1;
        public int RequestId { get; set; }
        
        public override string ToString()
        {
            if (Odobren)
            {
                return $"Zahtev odobren - ID: {RequestId}";
            }
            else
            {
                var korekcija = (NovaX != -1 && NovaY != -1 && NovaZ != -1) ? 
                    $" Predlog korekcije: ({NovaX},{NovaY},{NovaZ})" : "";
                return $"Zahtev odbačen: {Poruka}{korekcija}";
            }
        }
    }
}
