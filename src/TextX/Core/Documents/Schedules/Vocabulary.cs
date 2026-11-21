using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TextX.Core.Documents.Schedules
{
    /// <summary>A recognised term: what it is called in English, and the words found in the text.</summary>
    public sealed class TermMatch
    {
        public string Canonical;
        public string Found;
        public int Index;
    }

    /// <summary>
    /// Words of architectural and joinery drawings in English, French, German, Spanish, Italian and
    /// Dutch: materials, finishes, colours, products, title-block fields and schedule headings.
    /// Matching ignores case and accents. The first word of each list is the English name.
    /// </summary>
    public sealed class Vocabulary
    {
        private readonly List<KeyValuePair<string, string>> _terms = new List<KeyValuePair<string, string>>(); // folded term, canonical
        private readonly Dictionary<string, string> _byNorm = new Dictionary<string, string>(StringComparer.Ordinal);
        private Regex _regex;

        public Vocabulary(params string[] groups)
        {
            foreach (var g in groups)
            {
                var words = g.Split('|').Select(w => w.Trim()).Where(w => w.Length > 0).ToList();
                if (words.Count == 0) continue;
                foreach (var w in words)
                {
                    var f = Fold(w);
                    _terms.Add(new KeyValuePair<string, string>(f, words[0]));
                    var n = Norm(f);
                    if (!_byNorm.ContainsKey(n)) _byNorm[n] = words[0];
                }
            }
            var alternatives = _terms.Select(t => t.Key).Distinct().OrderByDescending(t => t.Length).Select(Pattern);
            _regex = new Regex(@"(?<![\p{L}\p{N}])(?:" + string.Join("|", alternatives) + @")(?![\p{L}\p{N}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }

        private static string Pattern(string folded)
        {
            // spaces and hyphens inside a term match any run of spaces / hyphens
            var parts = Regex.Split(folded, @"[\s\-]+").Select(Regex.Escape);
            return string.Join(@"[\s\-]*", parts);
        }

        /// <summary>All terms in the text, left to right, without overlaps.</summary>
        public List<TermMatch> Find(string text)
        {
            var result = new List<TermMatch>();
            if (string.IsNullOrEmpty(text)) return result;
            var folded = Fold(text);
            if (folded.Length != text.Length) return FindSlow(text);
            foreach (Match m in _regex.Matches(folded))
                result.Add(new TermMatch { Canonical = CanonicalOf(m.Value), Found = text.Substring(m.Index, m.Length), Index = m.Index });
            return result;
        }

        // folding changed the length (rare ligatures): match word by word on the folded text only
        private List<TermMatch> FindSlow(string text)
        {
            var result = new List<TermMatch>();
            foreach (Match m in _regex.Matches(Fold(text)))
                result.Add(new TermMatch { Canonical = CanonicalOf(m.Value), Found = m.Value, Index = -1 });
            return result;
        }

        public TermMatch First(string text)
        {
            return Find(text).FirstOrDefault();
        }

        public bool Contains(string text)
        {
            return !string.IsNullOrEmpty(text) && _regex.IsMatch(Fold(text));
        }

        /// <summary>True when the whole text (ignoring punctuation and a trailing colon) is one term.</summary>
        public string Exactly(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = Norm(Fold(text).Trim().TrimEnd(':', '-', '\u2013', ' ', '/').Trim());
            string canonical;
            if (_byNorm.TryGetValue(t, out canonical)) return canonical;
            if (t.EndsWith(".", StringComparison.Ordinal) && _byNorm.TryGetValue(t.TrimEnd('.'), out canonical)) return canonical;
            return null;
        }

        private string CanonicalOf(string foldedMatch)
        {
            string canonical;
            return _byNorm.TryGetValue(Norm(foldedMatch), out canonical) ? canonical : foldedMatch;
        }

        private static string Norm(string folded)
        {
            return Regex.Replace(folded, @"[\s\-]+", string.Empty);
        }

        /// <summary>Lower case without accents ("Chêne" → "chene", "Größe" → "grosse").</summary>
        public static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (var c in d)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                if (c == 'ß') { sb.Append("ss"); continue; }
                if (c == 'œ' || c == 'Œ') { sb.Append("oe"); continue; }
                if (c == 'æ' || c == 'Æ') { sb.Append("ae"); continue; }
                if (c == 'ø' || c == 'Ø') { sb.Append('o'); continue; }
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        // ------------------------------------------------------------------ word lists

        public static readonly Vocabulary Materials = new Vocabulary(
            "stainless steel|inox|acier inoxydable|acier inox|edelstahl|acero inoxidable|acciaio inox|acciaio inossidabile|roestvrij staal|rvs",
            "oak|chene|eiche|eichen|roble|rovere|eik|eiken",
            "walnut|noyer|nussbaum|nogal|noce|noten",
            "ash|frene|esche|fresno|frassino|essen",
            "beech|hetre|buche|haya|faggio|beuken",
            "maple|erable|ahorn|arce|esdoorn",
            "birch|bouleau|birke|abedul|betulla|berken",
            "pine|pin|kiefer|pino|grenen",
            "teak|teck|teca|teakhout",
            "cherry|merisier|cerisier|kirsche|kirschbaum|cerezo|ciliegio|kersen",
            "wenge",
            "bamboo|bambou|bambus|bambu|bamboe",
            "rattan|rotin|rattan|ratan|midollino",
            "cork|liege|kork|corcho|sughero|kurk",
            "solid wood|bois massif|massivholz|madera maciza|legno massello|massief hout",
            "wood|timber|bois|holz|madera|legno|hout",
            "veneer|placage|plaque|furnier|chapa|chapado|impiallacciatura|impiallacciato|fineer",
            "MDF|fibre board|fibreboard|fiberboard|medium density fibreboard",
            "plywood|contreplaque|contre-plaque|sperrholz|multiplex|contrachapado|multistrato",
            "particleboard|chipboard|panneau de particules|agglomere|spanplatte|aglomerado|truciolare|spaanplaat",
            "OSB",
            "melamine|melamine faced|melamine|melamin|melamina|melaminico",
            "HPL|high pressure laminate|stratifie haute pression",
            "laminate|stratifie|schichtstoff|laminat|laminado|laminato",
            "solid surface|corian|hi-macs|himacs|krion",
            "quartz|quartz composite|quarz|cuarzo|quarzo|kwarts|silestone|caesarstone",
            "granite|granit|granito|graniet",
            "marble|marbre|marmor|marmol|marmo|marmer",
            "travertine|travertin|travertino",
            "limestone|pierre calcaire|kalkstein|piedra caliza|pietra calcarea|kalksteen",
            "slate|ardoise|schiefer|pizarra|ardesia|leisteen",
            "sintered stone|ceramique frittee|dekton|neolith|lapitec",
            "stone|pierre|naturstein|stein|piedra|pietra|steen|natuursteen",
            "terrazzo|granito",
            "concrete|beton|hormigon|calcestruzzo|cemento",
            "microcement|microcement|microbeton|mikrozement|microcemento",
            "glass|verre|glas|vidrio|cristal|vetro",
            "mirror|miroir|spiegel|espejo|specchio",
            "steel|acier|stahl|acero|acciaio|staal",
            "aluminium|aluminum|alu|aluminio|alluminio",
            "brass|laiton|messing|laton|ottone",
            "bronze|bronce|bronzo|brons",
            "copper|cuivre|kupfer|cobre|rame|koper",
            "zinc|zink|cinc|zinco",
            "iron|fer|eisen|hierro|ferro|ijzer",
            "leather|cuir|leder|cuero|pelle|piel",
            "fabric|textile|tissu|stoff|textil|tela|tessuto|stof",
            "wool|laine|wolle|lana|wol",
            "felt|feutre|filz|fieltro|feltro|vilt",
            "velvet|velours|samt|terciopelo|velluto|fluweel",
            "linen|lin|leinen|lino|linnen",
            "ceramic|ceramique|keramik|ceramica|keramiek",
            "porcelain|porcelaine|porzellan|porcelanico|porcelanato|gres porcellanato|porselein",
            "tile|carrelage|carreau|fliese|fliesen|baldosa|azulejo|piastrella|tegel",
            "mosaic|mosaique|mosaik|mosaico|mozaiek",
            "vinyl|vinyle|vinilo|vinile|LVT",
            "linoleum|lino",
            "carpet|moquette|teppich|alfombra|tapijt",
            "rubber|caoutchouc|gummi|caucho|gomma",
            "acrylic|acrylique|acryl|acrilico|plexiglas|PMMA",
            "polycarbonate|polycarbonat|policarbonato",
            "PVC",
            "plasterboard|gypsum board|drywall|placo|plaque de platre|gipskarton|rigips|placa de yeso|pladur|cartongesso|gipsplaat",
            "plaster|platre|putz|gips|yeso|intonaco|gesso|pleister",
            "render|enduit|crepi|revoque|stucco");

        public static readonly Vocabulary Finishes = new Vocabulary(
            "high gloss|haute brillance|tres brillant|hochglanz|alto brillo|lucido|hoogglans",
            "matt|matte|mat|mate|opaco|opaca|matt lacquer",
            "gloss|glossy|brillant|glanzend|glanz|brillo|brillante|glans",
            "satin|satine|seidenmatt|seidenglanz|satinado|satinato|zijdeglans",
            "lacquered|lacquer|laque|lacke|lackiert|lack|lacado|laca|laccato|lacca|gelakt|lak",
            "powder coated|thermolaque|poudre|pulverbeschichtet|lacado en polvo|verniciato a polvere|gepoedercoat",
            "painted|peint|peinture|gestrichen|pintado|verniciato|geverfd",
            "varnished|vernis|verni|lackiert|barnizado|verniciato trasparente|gevernist",
            "oiled|huile|geolt|aceitado|oliato|geolied",
            "waxed|cire|gewachst|encerado|cerato|gewaxt",
            "stained|teinte|lasure|gebeizt|tenido|tinto|gebeitst",
            "polished|poli|polie|poliert|pulido|lucidato|gepolijst",
            "brushed|brosse|gebuerstet|gebuerstet|geburstet|cepillado|spazzolato|geborsteld",
            "honed|adouci|geschliffen|apomazado|levigato|gezoet",
            "sandblasted|sable|sandgestrahlt|chorreado|sabbiato|gezandstraald",
            "anodised|anodized|anodise|eloxiert|anodizado|anodizzato|geanodiseerd",
            "chrome|chrome plated|chrome|verchromt|cromado|cromato|verchroomd",
            "nickel|nickele|vernickelt|niquelado|nichelato",
            "brushed nickel|nickel brosse",
            "galvanised|galvanized|galvanise|verzinkt|galvanizado|zincato",
            "textured|texture|strukturiert|texturizado|strutturato|getextureerd",
            "fumed|smoked|fume|geraeuchert|geraucht|ahumado|affumicato|gerookt",
            "bleached|blanchi|gebleicht|blanqueado|sbiancato|gebleekt",
            "natural|naturel|naturelle|natur|natural|naturale|naturel",
            "raw|brut|roh|crudo|grezzo|ruw",
            "flamed|flamme|geflammt|flameado|fiammato|gevlamd",
            "leathered|cuir|lederoptik|apelazado|spazzolato pelle",
            "antique|patine|patiniert|envejecido|anticato",
            "frosted|depoli|satine verre|satiniert|mateado|acidato|gezuurd",
            "clear|transparent|incolore|klar|transparente|trasparente|helder",
            "tinted|teinte verre|getoent|tintado|colorato|getint",
            "edge banding|chant|chants|kante|kantenband|canto|bordo|kantenband",
            "anti-fingerprint|anti trace|anti fingerprint|antifingerprint");

        public static readonly Vocabulary Colours = new Vocabulary(
            "white|blanc|blanche|weiss|blanco|blanca|bianco|bianca|wit",
            "black|noir|noire|schwarz|negro|negra|nero|nera|zwart",
            "grey|gray|gris|grise|grau|grigio|grijs",
            "anthracite|anthrazit|antracita|antracite|antraciet",
            "charcoal|charbon",
            "beige",
            "cream|creme|crema|creme",
            "ivory|ivoire|elfenbein|marfil|avorio|ivoor",
            "taupe",
            "sand|sable|sand|arena|sabbia|zand",
            "brown|brun|brune|marron|braun|marron|marrone|bruin",
            "red|rouge|rot|rojo|roja|rosso|rossa|rood",
            "blue|bleu|bleue|blau|azul|blu|blauw",
            "navy|marine|bleu marine|marineblau|azul marino|blu navy",
            "green|vert|verte|grun|gruen|verde|groen",
            "yellow|jaune|gelb|amarillo|giallo|geel",
            "orange|naranja|arancione|oranje",
            "pink|rose|rosa|roze",
            "terracotta|terre cuite|terrakotta|terracota",
            "gold|dore|doree|golden|dorado|dorato|goud",
            "silver|argent|argente|silber|plata|plateado|argento|zilver",
            "champagne",
            "copper colour|cuivre|kupferfarben",
            "bronze colour|bronze");

        public static readonly Vocabulary Products = new Vocabulary(
            "worktop|countertop|work top|plan de travail|arbeitsplatte|encimera|piano di lavoro|werkblad|aanrechtblad",
            "door|porte|tuer|tur|tuer|puerta|porta|deur",
            "drawer|tiroir|schublade|schubkasten|cajon|cassetto|lade",
            "handle|pull|poignee|griff|tirador|maniglia|handgreep|greep",
            "knob|bouton|knauf|pomo|pomolo|knop",
            "hinge|charniere|scharnier|bisagra|cerniera",
            "runner|coulisse|auszug|guia|guida|geleider",
            "cabinet|unit|carcass|carcase|caisson|meuble|element|schrank|korpus|mueble|armario|mobile|kast",
            "tall unit|colonne|hochschrank|columna|colonna|hoge kast",
            "wall unit|meuble haut|oberschrank|hangeschrank|mueble alto|pensile|bovenkast",
            "base unit|meuble bas|unterschrank|mueble bajo|onderkast",
            "shelf|shelving|etagere|tablette|regal|fachboden|estante|balda|ripiano|mensola|plank",
            "panel|panneau|paneel|platte|panel|pannello",
            "plinth|kickboard|socle|plinthe|sockel|zocalo|zoccolo|plint",
            "skirting|skirting board|plinthe murale|fussleiste|rodapie|battiscopa",
            "cornice|corniche|kranzleiste|cornisa|cornice",
            "splashback|backsplash|credence|fliesenspiegel|ruckwand|salpicadero|alzatina|achterwand",
            "frame|cadre|rahmen|marco|telaio|kader",
            "light|luminaire|lamp|lighting|eclairage|leuchte|lampe|beleuchtung|lampara|luminaria|lampada|apparecchio|verlichting",
            "spotlight|downlight|spot|einbaustrahler|foco|faretto",
            "LED strip|ruban led|bandeau led|led-streifen|tira led|striscia led",
            "switch|interrupteur|schalter|interruptor|interruttore|schakelaar",
            "socket|outlet|prise|steckdose|enchufe|presa|stopcontact",
            "tap|faucet|mixer|robinet|mitigeur|armatur|wasserhahn|grifo|rubinetto|miscelatore|kraan",
            "sink|evier|spule|fregadero|lavello|spoelbak",
            "basin|washbasin|vasque|lavabo|waschbecken|lavabo|wastafel",
            "toilet|WC|toilette|cuvette|toilette|inodoro|vaso|toilet",
            "shower|douche|dusche|ducha|doccia",
            "bath|bathtub|baignoire|badewanne|banera|vasca|bad",
            "oven|four|backofen|horno|forno|oven",
            "hob|cooktop|plaque de cuisson|table de cuisson|kochfeld|placa|piano cottura|kookplaat",
            "extractor hood|hood|hotte|dunstabzug|abzugshaube|campana|cappa|afzuigkap",
            "fridge|refrigerator|refrigerateur|frigo|kuhlschrank|kuehlschrank|nevera|frigorifico|frigorifero|koelkast",
            "dishwasher|lave-vaisselle|geschirrspuler|geschirrspueler|lavavajillas|lavastoviglie|vaatwasser",
            "appliance|electromenager|gerat|electrodomestico|elettrodomestico|apparaat",
            "window|fenetre|fenster|ventana|finestra|raam",
            "partition|cloison|trennwand|tabique|parete divisoria|scheidingswand",
            "wall finish|revetement mural|wandbelag|revestimiento|rivestimento",
            "floor|flooring|sol|revetement de sol|parquet|boden|bodenbelag|suelo|pavimento|vloer",
            "ceiling|plafond|decke|techo|soffitto",
            "wall|mur|wand|pared|parete|muur",
            "stair|staircase|escalier|treppe|escalera|scala|trap",
            "handrail|balustrade|main courante|garde-corps|handlauf|gelander|pasamanos|barandilla|corrimano|ringhiera|leuning",
            "mirror|miroir|spiegel|espejo|specchio",
            "chair|chaise|fauteuil|stuhl|sessel|silla|sillon|sedia|poltrona|stoel",
            "stool|tabouret|hocker|taburete|sgabello|kruk",
            "table|desk|bureau|tisch|schreibtisch|mesa|escritorio|tavolo|scrivania|tafel",
            "sofa|canape|couch|divano|bank",
            "bed|lit|bett|cama|letto",
            "wardrobe|armoire|penderie|kleiderschrank|armario ropero|armadio|garderobe",
            "bench|banc|banquette|bank|banco|panca",
            "curtain|rideau|vorhang|cortina|tenda|gordijn",
            "blind|store|rollo|jalousie|persiana|estor|tapparella|zonwering",
            "rail|tringle|stange|barra|binario|rail",
            "cushion|coussin|kissen|cojin|cuscino|kussen",
            "rug|tapis|teppich|alfombra|tappeto|vloerkleed",
            "upholstery|tapisserie|garnissage|polsterung|tapiceria|tappezzeria|bekleding",
            "glazing|vitrage|verglasung|acristalamiento|vetrata|beglazing",
            "lock|serrure|schloss|cerradura|serratura|slot",
            "seal|joint|dichtung|junta|guarnizione|afdichting");

        /// <summary>Title block fields. Canonical names are the column labels shown to the user.</summary>
        public static readonly Vocabulary TitleFields = new Vocabulary(
            "Drawing number|drawing no|drawing nr|dwg no|dwg nr|drawing number|drg no|sheet no|sheet number|plan no|plan n|n plan|numero de plan|zeichnungsnummer|zeichnungs-nr|plan-nr|plano n|plano no|numero plano|tavola n|tavola|disegno n|tekeningnummer|tekening nr",
            "Drawing title|title|drawing title|titre|titre du plan|titel|bezeichnung|titulo|titolo|tekeningtitel",
            "Project|project|projet|projekt|proyecto|progetto|job|job name|scheme|bauvorhaben|obra|commessa",
            "Project number|project no|project nr|job no|job number|n projet|numero de projet|projektnummer|proyecto n|commessa n|projectnummer",
            "Client|client|owner|maitre d'ouvrage|maitre d ouvrage|bauherr|auftraggeber|cliente|promotor|committente|opdrachtgever",
            "Architect|architect|architects|architecte|architectes|architekt|architekten|arquitecto|arquitectos|architetto|architetti|architect",
            "Interior designer|interior designer|interior design|designer|architecte d'interieur|innenarchitekt|interiorista|disegnatore d'interni",
            "Contractor|contractor|main contractor|entreprise|entrepreneur|bauunternehmer|auftragnehmer|contratista|impresa|aannemer",
            "Manufacturer|manufacturer|fabricant|hersteller|fabricante|produttore|fabrikant",
            "Engineer|engineer|ingenieur|bureau d'etudes|ingeniero|ingegnere",
            "Drawn by|drawn by|drawn|dessine par|dessine|dessinateur|gezeichnet|gez|dibujado por|dibujado|disegnato da|disegnato|getekend",
            "Checked by|checked by|checked|verifie par|verifie|controle|gepruft|geprueft|revisado por|revisado|controllato da|controllato|gecontroleerd",
            "Approved by|approved by|approved|approuve par|approuve|valide|freigegeben|genehmigt|aprobado por|aprobado|approvato da|approvato|goedgekeurd",
            "Scale|scale|scales|echelle|massstab|massstabe|mstb|escala|scala|schaal",
            "Date|date|datum|fecha|data",
            "Revision|rev|revision|revisions|rev no|indice|ind|revision|index|aenderung|anderung|revisione|revisie",
            "Status|status|statut|etat|phase|stage|phase|stand|estado|fase|stato",
            "Sheet|sheet|feuille|folio|planche|blatt|hoja|foglio|blad",
            "Paper size|paper size|format|papierformat|tamano|formato",
            "Address|address|adresse|anschrift|direccion|indirizzo|adres|site address|lieu",
            "Room|room|piece|local|raum|zimmer|habitacion|sala|stanza|vertrek|ruimte",
            "Location|location|emplacement|ort|lage|ubicacion|posizione|locatie",
            "Units|units|unites|einheiten|unidades|unita|eenheden|dimensions in|cotes en|masse in|medidas en|quote in",
            "Telephone|tel|telephone|phone|tel.|telefon|telefono|telefoon",
            "Email|email|e-mail|mail|courriel|correo");

        /// <summary>Column headings of schedules and tables.</summary>
        public static readonly Vocabulary Headings = new Vocabulary(
            "Code|code|ref|ref.|reference|item no|item nr|item|tag|mark|key|no|nr|no.|n|n°|nº|id|pos|pos.|position|repere|type|codice|codigo|referencia|riferimento|kenmerk",
            "Description|description|designation|desc|beschreibung|bezeichnung|benennung|descripcion|denominacion|descrizione|omschrijving|name|nom|naam|product|produit|produkt|producto|prodotto|article|artikel|articulo|articolo|element|element",
            "Material|material|materials|materiau|materiaux|matiere|werkstoff|materiale|materiali|materiaal",
            "Finish|finish|finishes|finition|finitions|oberflache|oberflaeche|ausfuhrung|ausfuehrung|acabado|acabados|finitura|finiture|afwerking|surface",
            "Colour|colour|color|colours|colors|couleur|couleurs|teinte|farbe|farbton|color|colore|kleur|ral",
            "Size|size|sizes|dimension|dimensions|dims|dim|w x d x h|l x w x h|w x h|taille|format|abmessung|abmessungen|masse|groesse|grosse|medidas|dimensiones|tamano|dimensioni|misure|afmetingen|maat",
            "Width|width|w|largeur|l|breite|b|ancho|larghezza|breedte",
            "Height|height|h|hauteur|hohe|hoehe|alto|altura|altezza|hoogte",
            "Depth|depth|d|profondeur|p|tiefe|t|fondo|profundidad|profondita|diepte",
            "Length|length|longueur|lange|laenge|largo|longitud|lunghezza|lengte",
            "Thickness|thickness|thk|ep|epaisseur|starke|staerke|dicke|espesor|spessore|dikte",
            "Qty|qty|quantity|quantities|quantite|qte|nb|nombre|menge|anzahl|stk|stuck|stueck|cantidad|cant|quantita|qta|aantal|no off|no. off|nos",
            "Unit|unit|unite|u|einheit|eh|unidad|ud|unita|um|eenheid",
            "Manufacturer|manufacturer|supplier|brand|make|fabricant|fournisseur|marque|hersteller|lieferant|marke|fabricante|proveedor|marca|produttore|fornitore|fabrikant|leverancier|merk",
            "Model|model|model no|product code|art no|art. no|article no|modele|reference fabricant|modell|artikelnummer|art.-nr|modelo|modello|codice articolo|artikelnummer|typenummer",
            "Location|location|room|area|zone|emplacement|local|piece|raum|ort|ubicacion|zona|ubicazione|locale|locatie|ruimte",
            "Notes|notes|note|remarks|comments|remarques|observations|bemerkungen|bemerkung|hinweise|notas|observaciones|note|osservazioni|opmerkingen",
            "Price|price|unit price|prix|preis|precio|prezzo|prijs",
            "Total|total|totaal|totale|gesamt|montant|importe|importo");

        public static readonly Vocabulary NotesHeadings = new Vocabulary(
            "notes|note|general notes|remarks|remarques|notes generales|observations|anmerkungen|hinweise|allgemeine hinweise|bemerkungen|notas|notas generales|observaciones|note generali|annotazioni|opmerkingen|algemene opmerkingen");

        /// <summary>Tag prefixes common on drawings, with the kind of item they usually mark.</summary>
        public static readonly Dictionary<string, string> TagPrefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "D", "Door" }, { "DR", "Door" }, { "W", "Window" }, { "WD", "Window" },
            { "FF", "Floor finish" }, { "FL", "Floor finish" }, { "WF", "Wall finish" }, { "CF", "Ceiling finish" }, { "CL", "Ceiling" },
            { "L", "Light" }, { "LT", "Light" }, { "LF", "Light" }, { "LUM", "Light" }, { "E", "Electrical" }, { "EL", "Electrical" },
            { "SW", "Switch" }, { "SK", "Socket" }, { "PS", "Socket" },
            { "J", "Joinery" }, { "JN", "Joinery" }, { "JY", "Joinery" }, { "MW", "Joinery" }, { "CAB", "Cabinet" }, { "K", "Kitchen" },
            { "F", "Furniture" }, { "FUR", "Furniture" }, { "FN", "Furniture" }, { "FX", "Fixture" },
            { "AP", "Appliance" }, { "APP", "Appliance" }, { "EQ", "Equipment" },
            { "SN", "Sanitary" }, { "SAN", "Sanitary" }, { "S", "Sanitary" }, { "PL", "Plumbing" }, { "TP", "Tap" },
            { "HW", "Hardware" }, { "IR", "Ironmongery" }, { "H", "Handle" }, { "HD", "Handle" },
            { "M", "Material" }, { "MT", "Metal" }, { "MAT", "Material" }, { "PT", "Paint" }, { "P", "Paint / panel" },
            { "TL", "Tile" }, { "T", "Tile" }, { "ST", "Stone" }, { "WD-", "Wood" }, { "TM", "Timber" }, { "V", "Veneer" }, { "VN", "Veneer" },
            { "GL", "Glass" }, { "G", "Glass" }, { "MR", "Mirror" }, { "LAM", "Laminate" }, { "LM", "Laminate" },
            { "UP", "Upholstery" }, { "UPH", "Upholstery" }, { "FB", "Fabric" }, { "FAB", "Fabric" }, { "LE", "Leather" },
            { "CP", "Carpet" }, { "CPT", "Carpet" }, { "RG", "Rug" }, { "WC", "Wallcovering" }, { "WP", "Wallpaper" },
            { "SS", "Stainless steel" }, { "AL", "Aluminium" }, { "BR", "Brass" }, { "CU", "Curtain" }, { "BL", "Blind" },
        };
    }
}
