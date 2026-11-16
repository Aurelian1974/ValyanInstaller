namespace Erp.Licensing;

/// <summary>
/// Comutatoarele de severitate ale licențierii, cu valori diferite în dezvoltare și în produsul livrat.
/// </summary>
public static class LicensingPolicy
{
    /// <summary>
    /// Cifra de control a CUI-ului blochează generarea licenței (3.2).
    /// <para>
    /// Decizia lui Aurelian (16.11.2026): în timpul dezvoltării și al testelor se acceptă și
    /// CUI-uri cu cifră de control greșită — exemplele din specificație sunt de acest fel —
    /// dar <b>produsul finalizat nu trebuie să permită niciun CUI invalid</b>.
    /// </para>
    /// <para>
    /// ÎNAINTE DE LIVRARE se trece pe <c>true</c> (lista din secțiunea „Înainte de livrare”
    /// a CLAUDE.md). Atunci un CUI cu cifră de control greșită se marchează cu roșu și
    /// blochează generarea, exact ca în 3.2.
    /// </para>
    /// </summary>
    public static bool EnforceFiscalCodeCheckDigit => false;
}
