using System;

namespace Domain
{
    [Serializable]
    public class ZahtevZaUlazak
    {
        public Let Let { get; set; }
        public DateTime VremeZahteva { get; set; } = DateTime.Now;
        public string StatusZahteva { get; set; } = "Na čekanju"; // Na čekanju, Odobren, Odbačen
        
        public override string ToString()
        {
            return $"Zahtev za ulazak: {Let?.Letelica?.RegistracijaAviona} - Status: {StatusZahteva}";
        }
    }
}
