using System.Runtime.InteropServices;
using Rhino.PlugIns;

// Version, title, company and description are generated from VERSION and Directory.Build.props
// (see TextX.csproj). Only the attributes that never change live here.

// Plug-in description attributes (shown in Rhino's Plug-in Manager)
[assembly: PlugInDescription(DescriptionType.Address, "")]
[assembly: PlugInDescription(DescriptionType.Country, "")]
[assembly: PlugInDescription(DescriptionType.Email, "")]
[assembly: PlugInDescription(DescriptionType.Phone, "")]
[assembly: PlugInDescription(DescriptionType.Fax, "")]
[assembly: PlugInDescription(DescriptionType.Organization, "Text X")]
[assembly: PlugInDescription(DescriptionType.UpdateUrl, "")]
[assembly: PlugInDescription(DescriptionType.WebSite, "")]
[assembly: PlugInDescription(DescriptionType.Icon, "")]

[assembly: ComVisible(false)]

// The plug-in id. NEVER change it: Rhino stores settings and load state by id, and keeping the id
// of the 1.0 "Translator" preview lets Text X replace it and keep its settings.
[assembly: Guid("6f2c6b0e-4f7a-4e1b-9d6a-2b7f1d8a3c51")]
