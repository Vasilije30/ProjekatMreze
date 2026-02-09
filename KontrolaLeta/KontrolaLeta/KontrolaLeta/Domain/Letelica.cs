using System;

namespace Domain
{
    [Serializable]
    public class Letelica
    {
        public string NazivAviokompanije { get; set; }
        public string NazivGlavnogPilota { get; set; }
        public string RegistracijaAviona { get; set; }
        public int MaksimalanBrojPutnika { get; set; }
        public int TrenutanBrojPutnika { get; set; }

        public override string ToString()
        {
            return $"{NazivAviokompanije} - {RegistracijaAviona} (Pilot: {NazivGlavnogPilota}, Putnici: {TrenutanBrojPutnika}/{MaksimalanBrojPutnika})";
        }
    }
}
