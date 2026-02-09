using System;

namespace Domain
{
    [Serializable]
    public class Let
    {
        public int PocetnaX { get; set; }
        public int PocetnaY { get; set; }
        public int PocetnaZ { get; set; }
        public int KrajnjaX { get; set; }
        public int KrajnjaY { get; set; }
        public int KrajnjaZ { get; set; }
        public Letelica Letelica { get; set; }
        
        // Trenutna pozicija tokom leta
        public int TrenutnaX { get; set; }
        public int TrenutnaY { get; set; }
        public int TrenutnaZ { get; set; }
        
        // Dodatne informacije za praćenje
        public DateTime VremePocetka { get; set; } = DateTime.Now;
        public int BrojProdjeniSektora { get; set; } = 0;
        public int BrojIzmenaputanje { get; set; } = 0;

        public override string ToString()
        {
            return $"Let: {Letelica?.RegistracijaAviona} - Od ({PocetnaX},{PocetnaY},{PocetnaZ}) do ({KrajnjaX},{KrajnjaY},{KrajnjaZ}) - Trenutno: ({TrenutnaX},{TrenutnaY},{TrenutnaZ})";
        }
        
        public int BrojPreostalihSektora()
        {
            return Math.Abs(KrajnjaX - TrenutnaX) + Math.Abs(KrajnjaY - TrenutnaY) + Math.Abs(KrajnjaZ - TrenutnaZ);
        }
    }
}