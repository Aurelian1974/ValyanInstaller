---
applyTo: "**/*.xaml"
description: "Aspectul ecranelor WPF ale ErpSetup și ErpLicenseGen"
---

# Ecrane WPF

Doar controale WPF **standard**. Fără DevExpress, Telerik, Syncfusion sau orice altă bibliotecă de controale cu plată (R1).

Machetele de referință (de validat, nu bătute în cuie): `08-instalare-actualizare/ecrane/*.html` și `.png`. Dacă se schimbă un ecran, se actualizează și tabelul din secțiunea 5.2 a specificației.

## Culori și fonturi

| Rol | Valoare |
| --- | --- |
| Accent (butonul principal, pasul curent) | `#2f6fed`, apăsat `#2459c9`, fundal deschis `#eef3fd`, contur `#dbe6fc` |
| Coloana pașilor | `#0b1f3a`, text inactiv `#9fb2d1` |
| Text | `#1a2433`, secundar `#5b6676` |
| Linii și margini | `#e3e7ee`, câmpuri `#cfd6e1`, fundal ecran `#f5f7fa` |
| Succes / avertisment / eroare | `#1f9d63` / `#b07a00` / `#d14343` (fundaluri `#e3f5ec`, `#fbf1dc`, `#fbe9e9`) |
| Fonturi | Segoe UI; valorile tehnice (căi, amprente, parola temporară) cu Cascadia Mono |

Colțuri rotunjite 7–10 px, înălțimea butoanelor și a câmpurilor 36 px.

## Structura asistentului

Fereastră cu bară de titlu proprie, coloană de pași în stânga (numerotate, cu bifă verde pentru pașii încheiați) și, jos, bara cu „Anulează”, „← Înapoi”, „Înainte →”. Ecranele: 0 Bun venit (cu varianta „instalare existentă”), 1 Baza de date, 2 Site web, 3 Licență (cu varianta „licență invalidă”), 4 Verificare, 5 Instalare (cu varianta „eroare la un pas”), 6 Gata.

## Reguli de interacțiune

- Toate textele în română, cu diacritice.
- „Înainte →” e dezactivat cât timp ecranul are o eroare; pe ecranul 4 „Instalează” cere bifa „Am citit avertismentele” dacă există ⚠.
- Ecranul 5 arată fiecare pas cu starea lui (în așteptare, în curs, ✓, ✗) și durata, bara de progres, „Vezi jurnalul”, iar la eroare mesajul clar + „Reîncearcă”.
- Ecranul 6 afișează parola temporară o singură dată, cu font mare și buton „Copiază”; „Închide” e activ doar după bifa „Am notat parola”.
- Validările se arată pe câmp (contur roșu + mesaj), nu în `MessageBox`.
- Fereastra nu se blochează niciodată: operațiile lungi sunt `async`, cu progres și posibilitate de anulare acolo unde specificația o prevede.
