# Sistem Kontrole Leta

Simulacija rada sistema kontrole leta za nadzor i koordinaciju aviona unutar pravougaone sektorske mape vazdušnog prostora.

## Opis Sistema

Centralni server upravlja svim informacijama o sektorima vazdušnog prostora, uključujući stanje sektora i trenutnu zauzetost, dok avioni (klijenti) komuniciraju sa serverom putem UDP-a za zahteve za ulazak i TCP-a za koordinaciju tokom kretanja.

## Komponente

### 1. Server (Server.exe)
- Upravlja pravougaonom sektorskom mapom vazdušnog prostora
- Prima zahteve aviona za ulazak putem UDP protokola
- Koordinira kretanje aviona putem TCP protokola
- Detektuje i rešava konflikte između aviona
- Prikazuje vizuelnu reprezentaciju stanja vazdušnog prostora

### 2. Client/Avion (Client.exe)
- Šalje zahtev za ulazak u vazdušni prostor putem UDP-a
- Uspostavlja TCP vezu sa serverom za koordinaciju leta
- Prima instrukcije od servera za kretanje i preusmeravanje
- Prikazuje status leta u realnom vremenu

### 3. Domain
Sadrži klase:
- **Letelica**: Informacije o avionu (aviokompanija, pilot, registracija, putnici)
- **Let**: Koordinate početka i kraja leta, trenutna pozicija, statistike
- **Sektor**: Koordinate sektora, stanje zauzetosti, vremenski uslovi, ID leta koji zauzima sektor
- **VazdusniProstor**: Mapa sektora sa 3 nivoa visine
- **ZahtevZaUlazak**: Zahtev aviona za ulazak u vazdušni prostor
- **OdgovorServera**: Odgovor servera na zahtev (odobren/odbijen/korekcija koordinata)

## Pokretanje Sistema

**Napomena:** Solution fajl je preimenovan u `KontrolaLeta.sln`

### 1. Pokretanje Servera
```bash
cd Server/bin/Debug
Server.exe
```

Pri pokretanju servera:
- Unesite širinu vazdušnog prostora (broj sektora)
- Unesite visinu vazdušnog prostora (broj sektora)
- Server će kreirati 3D mapu sa 3 nivoa visine (Z=1,2,3)

### 2. Pokretanje Aviona (Klijenta)
```bash
cd Client/bin/Debug
Client.exe
```

Pri pokretanju klijenta:
1. Izaberite opciju "1" za zahtev za ulazak
2. Unesite podatke o letelici:
   - Naziv aviokompanije
   - Naziv glavnog pilota
   - Registracija aviona
   - Maksimalan broj putnika
   - Trenutni broj putnika
3. Unesite koordinate leta:
   - Početne koordinate (X, Y, Z)
   - Krajnje koordinate (X, Y, Z)

## Funkcionalnosti

### Server Funkcionalnosti:
- **Inicijalizacija vazdušnog prostora** sa N×M sektora i 3 nivoa visine
- **UDP komunikacija** za prijem zahteva za ulazak
- **TCP komunikacija** za koordinaciju kretanja aviona
- **Detekcija konflikata** između aviona
- **Automatsko preusmeravanje** aviona na slobodne visine ili susedne sektore
- **Vizualizacija mape** sa trenutnim stanjem sektora
- **Praćenje aktivnih letova** u realnom vremenu

### Client Funkcionalnosti:
- **UDP zahtev** za ulazak u vazdušni prostor
- **TCP koordinacija** sa serverom tokom leta
- **Automatsko kretanje** ka odredištu
- **Prijem instrukcija** za preusmeravanje
- **Prikaz statusa leta**:
  - Trenutna pozicija
  - Broj pređenih sektora
  - Broj preostalih sektora do cilja
  - Broj izmena putanje
  - Trajanje leta

## Algoritmi

### Detekcija Konflikata:
1. Server proverava da li je ciljani sektor zauzet ili ima abnormalne vremenske uslove
2. Ako je zauzet od drugog aviona, poredi se broj putnika:
   - Avion sa **manjim brojem putnika** se preusmerava
   - Avion sa većim brojem putnika dobija prioritet
3. Preusmeravanje: prvo se pokušava alternativna visina (Z koordinata)
4. Ako su sve visine zauzete, traži se najbliži slobodan susedni sektor u smeru cilja
5. Server šalje REDIRECT instrukciju avionu koji se preusmerava

### Kretanje Aviona:
1. Avion se kreće korak po korak ka cilju
2. Prioritet: X koordinata → Y koordinata → Z koordinata
3. Server potvrđuje svaki potez ili šalje preusmeravanje
4. Let se završava kada avion stigne do odredišta

## Vizualizacija

### Mapa Sektora:
- `[]` - Slobodan sektor
- `A1`, `A2`... - Sektor zauzet avionom (broj odgovara ID-u leta)
- `X` - Sektor sa abnormalnim vremenskim uslovima

### Server Prikaz:
- Lista aktivnih letova sa trenutnim pozicijama
- Lista zahteva za ulazak
- 2D mapa za svaki nivo visine (1, 2, 3)
- Legenda simbola

### Client Prikaz:
- Informacije o avionu i pilotu
- Trenutna pozicija i odredište
- Statistike leta (pređeni sektori, izmene putanje, trajanje)

## Tehnički Detalji

- **Protokoli**: UDP za zahteve, TCP za koordinaciju
- **Serijalizacija**: BinaryFormatter za razmenu objekata
- **Soketi**: System.Net.Sockets.Socket za UDP i TCP komunikaciju
- **Multipleksiranje**: Socket.Poll() za obradu više konekcija u jednoj petlji
- **Threading**: Asinhrono ažuriranje stanja i komunikacija
- **Port**: UDP 15001, TCP 15002
- **Framework**: .NET Framework 4.7.2

## Primer Rada

1. **Server** se pokreće i kreira vazdušni prostor 10×10 sa 3 nivoa
2. **Avion 1** traži let od (0,0,1) do (5,5,1)
3. Server odobrava zahtev i dodeljuje ID leta
4. Avion uspostavlja TCP vezu i počinje kretanje
5. **Avion 2** traži isti početni sektor
6. Server preusmerava Avion 2 na visinu Z=2
7. Oba aviona se kreću koordinisano ka svojim odredištima
8. Server kontinuirano ažurira mapu i prikazuje stanje

Sistem obezbeđuje bezbedno kretanje aviona, detekciju i rešavanje potencijalnih konflikata, kao i pregled trenutnog stanja vazdušnog prostora u realnom vremenu.
