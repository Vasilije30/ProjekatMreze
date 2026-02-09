using System;

namespace Domain
{
    [Serializable]
    public class Sektor
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public bool Zauzet { get; set; } = false;
        public bool AbnormalniVremenskiUslovi { get; set; } = false;
        public string ZauzetOdAviona { get; set; } = "";
        public int ZauzetOdLetaId { get; set; } = 0;

        public override string ToString()
        {
            var status = Zauzet ? $"Zauzet ({ZauzetOdAviona})" : "Slobodan";
            var vremenskiUslovi = AbnormalniVremenskiUslovi ? "Abnormalni" : "Normalni";
            return $"Sektor ({X},{Y},{Z}) - {status} - Vremenski uslovi: {vremenskiUslovi}";
        }

        public string GetDisplaySymbol()
        {
            if (Zauzet && ZauzetOdLetaId > 0)
                return $"A{ZauzetOdLetaId}";
            if (AbnormalniVremenskiUslovi)
                return " X";
            return "[]";
        }
    }
}
