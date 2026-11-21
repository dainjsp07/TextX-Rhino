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
            var terms = _terms.Select(t => t.Key).Distinct().OrderByDescending(t => t.Length).ToList();
            var alternatives = terms.Select(Pattern);
            // Hebrew attaches "and", "that", "in", "to", "from", "as", "the" to the next word (ופורמייקה, בחיפוי,
            // מהסנדוויץ). Only words of three letters or more take them: short ones hide inside other words
            // (בד in בלבד).
            var prefixed = terms.Where(t => t.Any(c => c >= '\u05D0' && c <= '\u05EA') && t.Count(char.IsLetter) >= 3).Select(Pattern).ToList();
            const string HebrewPrefix = "(?:\u05D5?\u05E9?(?:\u05DE\u05D4|[\u05D1\u05DC\u05DE\u05D4\u05DB])|\u05D5?\u05E9|\u05D5)-?";
            var body = prefixed.Count == 0
                ? "(?<t>" + string.Join("|", alternatives) + ")"
                : "(?:" + HebrewPrefix + "(?<t>" + string.Join("|", prefixed) + ")|(?<t>" + string.Join("|", alternatives) + "))";
            _regex = new Regex(@"(?<![\p{L}\p{N}])" + body + @"(?![\p{L}\p{N}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
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
            {
                var t = m.Groups["t"];
                result.Add(new TermMatch { Canonical = CanonicalOf(t.Value), Found = text.Substring(t.Index, t.Length), Index = t.Index });
            }
            return result;
        }

        // folding changed the length (rare ligatures): match word by word on the folded text only
        private List<TermMatch> FindSlow(string text)
        {
            var result = new List<TermMatch>();
            foreach (Match m in _regex.Matches(Fold(text)))
                result.Add(new TermMatch { Canonical = CanonicalOf(m.Groups["t"].Value), Found = m.Groups["t"].Value, Index = -1 });
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
            var t = Norm(Fold(text).Trim().Trim(':', '-', '\u2013', ' ', '/').Trim());
            if (t.Length == 0) return null;
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
                if (c == '\u05F4' || c == '\u201D' || c == '\u201C') { sb.Append('"'); continue; }   // Hebrew gershayim, curly quotes
                if (c == '\u05F3' || c == '\u2019' || c == '\u2018') { sb.Append('\''); continue; } // geresh, apostrophes
                if (c == 'œ' || c == 'Œ') { sb.Append("oe"); continue; }
                if (c == 'æ' || c == 'Æ') { sb.Append("ae"); continue; }
                if (c == 'ø' || c == 'Ø') { sb.Append('o'); continue; }
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        // ------------------------------------------------------------------ word lists

        public static readonly Vocabulary Materials = new Vocabulary(
            "stainless steel|inox|acier inoxydable|acier inox|edelstahl|acero inoxidable|acciaio inox|acciaio inossidabile|roestvrij staal|rvs|נירוסטה|נירוסטה מוברשת|פלב\"ם",
            "oak|chene|eiche|eichen|roble|rovere|eik|eiken|אלון",
            "walnut|noyer|nussbaum|nogal|noce|noten|אגוז",
            "ash|frene|esche|fresno|frassino|essen",
            "beech|hetre|buche|haya|faggio|beuken|אשור",
            "maple|erable|ahorn|arce|esdoorn",
            "birch|bouleau|birke|abedul|betulla|berken",
            "pine|pin|kiefer|pino|grenen|אורן",
            "teak|teck|teca|teakhout|טיק",
            "cherry|merisier|cerisier|kirsche|kirschbaum|cerezo|ciliegio|kersen",
            "wenge",
            "bamboo|bambou|bambus|bambu|bamboe",
            "rattan|rotin|rattan|ratan|midollino",
            "cork|liege|kork|corcho|sughero|kurk|שעם",
            "solid wood|bois massif|massivholz|madera maciza|legno massello|massief hout|עץ מלא|עץ מסיבי",
            "wood|timber|bois|holz|madera|legno|hout|עץ",
            "veneer|placage|plaque|furnier|chapa|chapado|impiallacciatura|impiallacciato|fineer|פורניר",
            "sandwich board|sandwich panel|blockboard|סנדוויץ|סנדוויץ'|סנדביץ|סנדביץ'",
            "sheet metal|metal sheet|פח|פח מכופף",
            "MDF|fibre board|fibreboard|fiberboard|medium density fibreboard",
            "plywood|contreplaque|contre-plaque|sperrholz|multiplex|contrachapado|multistrato|לבידה|דיקט",
            "particleboard|chipboard|panneau de particules|agglomere|spanplatte|aglomerado|truciolare|spaanplaat|סיבית|שבבית",
            "OSB",
            "melamine|melamine faced|melamine|melamin|melamina|melaminico|מלמין",
            "HPL|high pressure laminate|stratifie haute pression",
            "laminate|stratifie|schichtstoff|laminat|laminado|laminato|פורמייקה|פורמיקה|טרספה",
            "solid surface|corian|hi-macs|himacs|krion|קוריאן",
            "quartz|quartz composite|quarz|cuarzo|quarzo|kwarts|silestone|caesarstone|קוורץ|אבן קיסר",
            "granite|granit|granito|graniet|גרניט",
            "marble|marbre|marmor|marmol|marmo|marmer|שיש",
            "travertine|travertin|travertino",
            "limestone|pierre calcaire|kalkstein|piedra caliza|pietra calcarea|kalksteen",
            "slate|ardoise|schiefer|pizarra|ardesia|leisteen",
            "sintered stone|ceramique frittee|dekton|neolith|lapitec",
            "stone|pierre|naturstein|stein|piedra|pietra|steen|natuursteen|אבן",
            "terrazzo|granito|טרצו",
            "concrete|beton|hormigon|calcestruzzo|cemento|בטון",
            "microcement|microcement|microbeton|mikrozement|microcemento",
            "glass|verre|glas|vidrio|cristal|vetro|זכוכית",
            "mirror|miroir|spiegel|espejo|specchio|מראה|מראה",
            "steel|acier|stahl|acero|acciaio|staal|פלדה",
            "aluminium|aluminum|alu|aluminio|alluminio|אלומיניום",
            "brass|laiton|messing|laton|ottone|פליז",
            "bronze|bronce|bronzo|brons",
            "copper|cuivre|kupfer|cobre|rame|koper|נחושת",
            "zinc|zink|cinc|zinco",
            "iron|fer|eisen|hierro|ferro|ijzer|ברזל",
            "leather|cuir|leder|cuero|pelle|piel|עור",
            "fabric|textile|tissu|stoff|textil|tela|tessuto|stof|בד|אריג",
            "wool|laine|wolle|lana|wol",
            "felt|feutre|filz|fieltro|feltro|vilt",
            "velvet|velours|samt|terciopelo|velluto|fluweel",
            "linen|lin|leinen|lino|linnen",
            "ceramic|ceramique|keramik|ceramica|keramiek|קרמיקה",
            "porcelain|porcelaine|porzellan|porcelanico|porcelanato|gres porcellanato|porselein|פורצלן|גרניט פורצלן",
            "tile|carrelage|carreau|fliese|fliesen|baldosa|azulejo|piastrella|tegel|אריח|אריחים",
            "mosaic|mosaique|mosaik|mosaico|mozaiek",
            "vinyl|vinyle|vinilo|vinile|LVT|ויניל",
            "linoleum|lino",
            "carpet|moquette|teppich|alfombra|tapijt|שטיח",
            "rubber|caoutchouc|gummi|caucho|gomma|גומי",
            "acrylic|acrylique|acryl|acrilico|plexiglas|PMMA|אקריל|פרספקס",
            "polycarbonate|polycarbonat|policarbonato",
            "PVC",
            "plasterboard|gypsum board|drywall|placo|plaque de platre|gipskarton|rigips|placa de yeso|pladur|cartongesso|gipsplaat|גבס|לוח גבס|לוחות גבס",
            "plaster|platre|putz|gips|yeso|intonaco|gesso|pleister|טיח",
            "render|enduit|crepi|revoque|stucco");

        public static readonly Vocabulary Finishes = new Vocabulary(
            "high gloss|haute brillance|tres brillant|hochglanz|alto brillo|lucido|hoogglans|היי גלוס|מבריק מאוד",
            "matt|matte|mat|mate|opaco|opaca|matt lacquer|מט|מאט",
            "gloss|glossy|brillant|glanzend|glanz|brillo|brillante|glans|מבריק",
            "satin|satine|seidenmatt|seidenglanz|satinado|satinato|zijdeglans",
            "lacquered|lacquer|laque|lacke|lackiert|lack|lacado|laca|laccato|lacca|gelakt|lak|לכה|צבוע בלכה",
            "powder coated|thermolaque|poudre|pulverbeschichtet|lacado en polvo|verniciato a polvere|gepoedercoat|צבע בתנור|צביעה בתנור|צבוע בתנור|אלקטרוסטטי",
            "painted|peint|peinture|gestrichen|pintado|verniciato|geverfd|צבוע|צביעה",
            "varnished|vernis|verni|lackiert|barnizado|verniciato trasparente|gevernist",
            "oiled|huile|geolt|aceitado|oliato|geolied|שמן|בשמן",
            "waxed|cire|gewachst|encerado|cerato|gewaxt",
            "stained|teinte|lasure|gebeizt|tenido|tinto|gebeitst",
            "polished|poli|polie|poliert|pulido|lucidato|gepolijst|מלוטש|מלוטשת",
            "brushed|brosse|gebuerstet|gebuerstet|geburstet|cepillado|spazzolato|geborsteld|מוברש|מוברשת",
            "honed|adouci|geschliffen|apomazado|levigato|gezoet",
            "sandblasted|sable|sandgestrahlt|chorreado|sabbiato|gezandstraald",
            "anodised|anodized|anodise|eloxiert|anodizado|anodizzato|geanodiseerd|אנודייז|מאולגן",
            "chrome|chrome plated|chrome|verchromt|cromado|cromato|verchroomd|כרום|מכורם",
            "nickel|nickele|vernickelt|niquelado|nichelato",
            "brushed nickel|nickel brosse",
            "galvanised|galvanized|galvanise|verzinkt|galvanizado|zincato",
            "textured|texture|strukturiert|texturizado|strutturato|getextureerd",
            "fumed|smoked|fume|geraeuchert|geraucht|ahumado|affumicato|gerookt",
            "bleached|blanchi|gebleicht|blanqueado|sbiancato|gebleekt",
            "natural|naturel|naturelle|natur|natural|naturale|naturel|טבעי|טבעית",
            "raw|brut|roh|crudo|grezzo|ruw",
            "flamed|flamme|geflammt|flameado|fiammato|gevlamd",
            "leathered|cuir|lederoptik|apelazado|spazzolato pelle",
            "antique|patine|patiniert|envejecido|anticato",
            "frosted|depoli|satine verre|satiniert|mateado|acidato|gezuurd|חלבי|חלבית|אמבוס",
            "clear|transparent|incolore|klar|transparente|trasparente|helder|שקוף|שקופה",
            "tinted|teinte verre|getoent|tintado|colorato|getint",
            "edge banding|chant|chants|kante|kantenband|canto|bordo|kantenband",
            "extra clear|low iron|אקסטרה קליר|אקסטרה-קליר",
            "anti-fingerprint|anti trace|anti fingerprint|antifingerprint");

        public static readonly Vocabulary Colours = new Vocabulary(
            "white|blanc|blanche|weiss|blanco|blanca|bianco|bianca|wit|לבן|לבנה",
            "black|noir|noire|schwarz|negro|negra|nero|nera|zwart|שחור|שחורה",
            "grey|gray|gris|grise|grau|grigio|grijs|אפור|אפורה",
            "anthracite|anthrazit|antracita|antracite|antraciet|אנתרציט",
            "charcoal|charbon",
            "beige|בז'|בז",
            "cream|creme|crema|creme",
            "ivory|ivoire|elfenbein|marfil|avorio|ivoor",
            "taupe",
            "sand|sable|sand|arena|sabbia|zand",
            "brown|brun|brune|marron|braun|marron|marrone|bruin|חום|חומה",
            "red|rouge|rot|rojo|roja|rosso|rossa|rood|אדום|אדומה",
            "blue|bleu|bleue|blau|azul|blu|blauw|כחול|כחולה",
            "navy|marine|bleu marine|marineblau|azul marino|blu navy",
            "green|vert|verte|grun|gruen|verde|groen|ירוק|ירוקה",
            "yellow|jaune|gelb|amarillo|giallo|geel|צהוב|צהובה",
            "orange|naranja|arancione|oranje",
            "pink|rose|rosa|roze",
            "terracotta|terre cuite|terrakotta|terracota",
            "gold|dore|doree|golden|dorado|dorato|goud|זהב|מוזהב",
            "silver|argent|argente|silber|plata|plateado|argento|zilver|כסף|כסוף|כסופה",
            "champagne",
            "copper colour|cuivre|kupferfarben",
            "bronze colour|bronze");

        public static readonly Vocabulary Products = new Vocabulary(
            "counter|reception desk|reception counter|דלפק|דלפק קבלה",
            "kitchen|kitchenette|מטבח|מטבחון",
            "cladding|חיפוי|חיפויים",
            "niche|נישה",
            "worktop|countertop|work top|plan de travail|arbeitsplatte|encimera|piano di lavoro|werkblad|aanrechtblad|משטח|משטח עבודה",
            "door|porte|tuer|tur|tuer|puerta|porta|deur|דלת|דלתות",
            "drawer|tiroir|schublade|schubkasten|cajon|cassetto|lade|מגירה|מגירות|מגרות",
            "handle|pull|poignee|griff|tirador|maniglia|handgreep|greep|ידית|ידיות",
            "knob|bouton|knauf|pomo|pomolo|knop",
            "hinge|charniere|scharnier|bisagra|cerniera|ציר|צירים",
            "runner|coulisse|auszug|guia|guida|geleider",
            "cabinet|unit|carcass|carcase|caisson|meuble|element|schrank|korpus|mueble|armario|mobile|kast|ארון|ארונות",
            "tall unit|colonne|hochschrank|columna|colonna|hoge kast",
            "wall unit|meuble haut|oberschrank|hangeschrank|mueble alto|pensile|bovenkast",
            "base unit|meuble bas|unterschrank|mueble bajo|onderkast",
            "shelf|shelving|etagere|tablette|regal|fachboden|estante|balda|ripiano|mensola|plank|מדף|מדפים",
            "panel|panneau|paneel|platte|panel|pannello|פאנל|פנל",
            "plinth|kickboard|socle|plinthe|sockel|zocalo|zoccolo|plint|צוקל",
            "skirting|skirting board|plinthe murale|fussleiste|rodapie|battiscopa",
            "cornice|corniche|kranzleiste|cornisa|cornice",
            "splashback|backsplash|credence|fliesenspiegel|ruckwand|salpicadero|alzatina|achterwand",
            "frame|cadre|rahmen|marco|telaio|kader",
            "light|luminaire|lamp|lighting|eclairage|leuchte|lampe|beleuchtung|lampara|luminaria|lampada|apparecchio|verlichting|תאורה|גוף תאורה|פס תאורה",
            "spotlight|downlight|spot|einbaustrahler|foco|faretto",
            "LED strip|ruban led|bandeau led|led-streifen|tira led|striscia led",
            "switch|interrupteur|schalter|interruptor|interruttore|schakelaar",
            "socket|outlet|prise|steckdose|enchufe|presa|stopcontact",
            "tap|faucet|mixer|robinet|mitigeur|armatur|wasserhahn|grifo|rubinetto|miscelatore|kraan|ברז",
            "sink|evier|spule|fregadero|lavello|spoelbak|כיור",
            "basin|washbasin|vasque|lavabo|waschbecken|lavabo|wastafel",
            "toilet|WC|toilette|cuvette|toilette|inodoro|vaso|toilet",
            "shower|douche|dusche|ducha|doccia",
            "bath|bathtub|baignoire|badewanne|banera|vasca|bad",
            "oven|four|backofen|horno|forno|oven|תנור",
            "hob|cooktop|plaque de cuisson|table de cuisson|kochfeld|placa|piano cottura|kookplaat",
            "extractor hood|hood|hotte|dunstabzug|abzugshaube|campana|cappa|afzuigkap",
            "fridge|refrigerator|refrigerateur|frigo|kuhlschrank|kuehlschrank|nevera|frigorifico|frigorifero|koelkast|מקרר",
            "dishwasher|lave-vaisselle|geschirrspuler|geschirrspueler|lavavajillas|lavastoviglie|vaatwasser|מדיח|מדיח כלים",
            "appliance|electromenager|gerat|electrodomestico|elettrodomestico|apparaat",
            "window|fenetre|fenster|ventana|finestra|raam|חלון|חלונות",
            "partition|cloison|trennwand|tabique|parete divisoria|scheidingswand|מחיצה|מחיצות",
            "wall finish|revetement mural|wandbelag|revestimiento|rivestimento",
            "floor|flooring|sol|revetement de sol|parquet|boden|bodenbelag|suelo|pavimento|vloer|רצפה|ריצוף",
            "ceiling|plafond|decke|techo|soffitto|תקרה",
            "wall|mur|wand|pared|parete|muur|קיר|קירות",
            "stair|staircase|escalier|treppe|escalera|scala|trap",
            "handrail|balustrade|main courante|garde-corps|handlauf|gelander|pasamanos|barandilla|corrimano|ringhiera|leuning",
            "mirror|miroir|spiegel|espejo|specchio",
            "chair|chaise|fauteuil|stuhl|sessel|silla|sillon|sedia|poltrona|stoel|כיסא|כסא|כיסאות",
            "stool|tabouret|hocker|taburete|sgabello|kruk",
            "table|desk|bureau|tisch|schreibtisch|mesa|escritorio|tavolo|scrivania|tafel|שולחן|שולחנות",
            "sofa|canape|couch|divano|bank",
            "bed|lit|bett|cama|letto",
            "wardrobe|armoire|penderie|kleiderschrank|armario ropero|armadio|garderobe|ארון בגדים",
            "bench|banc|banquette|bank|banco|panca|ספסל|מושב",
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
            "Drawing number|drawing no|drawing nr|dwg no|dwg nr|drawing number|drg no|sheet no|sheet number|plan no|plan n|n plan|numero de plan|zeichnungsnummer|zeichnungs-nr|plan-nr|plano n|plano no|numero plano|tavola n|tavola|disegno n|tekeningnummer|tekening nr|שם התכנית|שם תכנית|מספר תכנית|מס' תכנית|מס תכנית|מספר שרטוט|מס' שרטוט|מספר גיליון|מס' גיליון",
            "Drawing title|title|drawing title|titre|titre du plan|titel|bezeichnung|titulo|titolo|tekeningtitel|תיאור|תיאור התכנית|שם השרטוט|כותרת|נושא|שם הגיליון",
            "Project|project|projet|projekt|proyecto|progetto|job|job name|scheme|bauvorhaben|obra|commessa|פרויקט|שם הפרויקט|הפרויקט",
            "Project number|project no|project nr|job no|job number|n projet|numero de projet|projektnummer|proyecto n|commessa n|projectnummer",
            "Client|client|owner|maitre d'ouvrage|maitre d ouvrage|bauherr|auftraggeber|cliente|promotor|committente|opdrachtgever|מזמין|המזמין|לקוח|יזם|היזם",
            "Architect|architect|architects|architecte|architectes|architekt|architekten|arquitecto|arquitectos|architetto|architetti|architect|אדריכל|אדריכלית|אדריכלים|משרד אדריכלים|מתכנן",
            "Interior designer|interior designer|interior design|designer|architecte d'interieur|innenarchitekt|interiorista|disegnatore d'interni|מעצב פנים|מעצבת פנים|עיצוב פנים",
            "Contractor|contractor|main contractor|entreprise|entrepreneur|bauunternehmer|auftragnehmer|contratista|impresa|aannemer|קבלן|קבלן ראשי",
            "Manufacturer|manufacturer|fabricant|hersteller|fabricante|produttore|fabrikant",
            "Engineer|engineer|ingenieur|bureau d'etudes|ingeniero|ingegnere|מהנדס|קונסטרוקטור",
            "Drawn by|drawn by|drawn|dessine par|dessine|dessinateur|gezeichnet|gez|dibujado por|dibujado|disegnato da|disegnato|getekend|שרטט|שורטט|שורטט ע\"י",
            "Checked by|checked by|checked|verifie par|verifie|controle|gepruft|geprueft|revisado por|revisado|controllato da|controllato|gecontroleerd|בדק|נבדק|בודק",
            "Approved by|approved by|approved|approuve par|approuve|valide|freigegeben|genehmigt|aprobado por|aprobado|approvato da|approvato|goedgekeurd|אישר|אושר|מאשר",
            "Scale|scale|scales|echelle|massstab|massstabe|mstb|escala|scala|schaal|קנ\"מ|קנה מידה|קנמ",
            "Date|date|datum|fecha|data|תאריך",
            "Revision|rev|revision|revisions|rev no|indice|ind|revision|index|aenderung|anderung|revisione|revisie|גרסה|מהדורה|מס' עדכון",
            "Status|status|statut|etat|phase|stage|phase|stand|estado|fase|stato|סטטוס|שלב",
            "Sheet|sheet|feuille|folio|planche|blatt|hoja|foglio|blad|גיליון|דף",
            "Paper size|paper size|format|papierformat|tamano|formato",
            "Address|address|adresse|anschrift|direccion|indirizzo|adres|site address|lieu|כתובת",
            "Room|room|piece|local|raum|zimmer|habitacion|sala|stanza|vertrek|ruimte|חדר|חלל",
            "Location|location|emplacement|ort|lage|ubicacion|posizione|locatie|מיקום",
            "Units|units|unites|einheiten|unidades|unita|eenheden|dimensions in|cotes en|masse in|medidas en|quote in",
            "Telephone|tel|telephone|phone|tel.|telefon|telefono|telefoon|טלפון|טל|phone|fax|mobile",
            "Website|website|web|www|social|אתר",
            "Email|email|e-mail|mail|courriel|correo|דוא\"ל|דואל|מייל");

        /// <summary>Column headings of schedules and tables.</summary>
        public static readonly Vocabulary Headings = new Vocabulary(
            "Code|code|ref|ref.|reference|item no|item nr|item|tag|mark|key|no|nr|no.|n|n°|nº|id|pos|pos.|position|repere|type|codice|codigo|referencia|riferimento|kenmerk",
            "Description|description|designation|desc|beschreibung|bezeichnung|benennung|descripcion|denominacion|descrizione|omschrijving|name|nom|naam|product|produit|produkt|producto|prodotto|article|artikel|articulo|articolo|element|element",
            "Material|material|materials|material type|materiau|materiaux|matiere|werkstoff|materiale|materiali|materiaal",
            "Finish|finish|finishes|finishing|finishings|surface finish|finition|finitions|oberflache|oberflaeche|ausfuhrung|ausfuehrung|acabado|acabados|finitura|finiture|afwerking|surface",
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

        /// <summary>Labels of an item's properties on a sheet (a finishes / hardware strip).</summary>
        public static readonly Vocabulary ItemFields = new Vocabulary(
            "Exterior finish|exterior finish|external finish|outside finish|finish outside|finition exterieure|aussen|acabado exterior|finitura esterna|גמר חוץ|גמר חיצוני|חזית הארון|חזית הארונות|חזיתות|חזיתות הארון",
            "Interior finish|interior finish|internal finish|inside finish|finish inside|finition interieure|innen|acabado interior|finitura interna|גמר פנים|גמר פנימי|פנים הארון|פנים הארונות",
            "Name|item name|item|name|designation|nom|nom de l'element|bezeichnung|nombre|nome|naam|שם פריט|שם הפריט|פריט",
            "Quantity|quantity|qty|quantite|qte|menge|anzahl|cantidad|quantita|aantal|כמות",
            "Size|size|dimensions|overall size|dimensions hors tout|abmessungen|masse|dimensiones|dimensioni|afmetingen|מידות|מידה",
            "Finish|finish|finishes|surface finish|finition|oberflache|oberflaeche|acabado|finitura|גמר|גימור",
            "Hardware|hardware|ironmongery|fittings|accessories|quincaillerie|beschlage|beschlaege|herrajes|ferramenta|פרזול|פירזול|אביזרים",
            "Material|material|materials|materiau|materiaux|werkstoff|materiale|חומר|חומרים|חומרי גמר",
            "Colour|colour|color|couleur|farbe|colore|גוון|גוונים",
            "Lighting|lighting|eclairage|beleuchtung|iluminacion|illuminazione|תאורה",
            "Electrical|electrical|electricity|electricite|elektro|electricidad|elettrico|חשמל");

        public static readonly Vocabulary NotesHeadings = new Vocabulary(
            "notes|note|general notes|remarks|remarques|notes generales|observations|anmerkungen|hinweise|allgemeine hinweise|bemerkungen|notas|notas generales|observaciones|note generali|annotazioni|opmerkingen|algemene opmerkingen|הערות|הערות כלליות|הערה");

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
