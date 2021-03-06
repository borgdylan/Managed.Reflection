/*
  The MIT License (MIT)
  Copyright (C) 2010-2013 Jeroen Frijters
  Copyright (C) 2011 Marek Safar

  Permission is hereby granted, free of charge, to any person obtaining a copy
  of this software and associated documentation files (the "Software"), to deal
  in the Software without restriction, including without limitation the rights
  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
  copies of the Software, and to permit persons to whom the Software is
  furnished to do so, subject to the following conditions:

  The above copyright notice and this permission notice shall be included in
  all copies or substantial portions of the Software.

  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
  SOFTWARE.
*/
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Managed.Reflection
{
    struct ParsedAssemblyName
    {
        internal string Name;
        internal string Version;
        internal string Culture;
        internal string PublicKeyToken;
        internal bool? Retargetable;
        internal ProcessorArchitecture ProcessorArchitecture;
        internal bool HasPublicKey;
        internal bool WindowsRuntime;
    }

    enum ParseAssemblyResult
    {
        OK,
        GenericError,
        DuplicateKey,
    }

    static class Fusion
    {
        static readonly Version FrameworkVersion = new Version(4, 0, 0, 0);
        static readonly Version FrameworkVersionNext = new Version(4, 1, 0, 0);
        static readonly Version SilverlightVersion = new Version(2, 0, 5, 0);
        static readonly Version SilverlightVersionMinimum = new Version(2, 0, 0, 0);
        static readonly Version SilverlightVersionMaximum = new Version(5, 9, 0, 0);
        const string PublicKeyTokenEcma = "b77a5c561934e089";
        const string PublicKeyTokenMicrosoft = "b03f5f7f11d50a3a";
        const string PublicKeyTokenSilverlight = "7cec85d7bea7798e";
        const string PublicKeyTokenWinFX = "31bf3856ad364e35";
        const string PublicKeyTokenNetStandard = "cc7b13ffcd2ddd51";

        // internal for use by mcs
        internal static bool CompareAssemblyIdentityPure(string assemblyIdentity1, bool unified1, string assemblyIdentity2, bool unified2, out AssemblyComparisonResult result)
        {
            ParsedAssemblyName name1;
            ParsedAssemblyName name2;

            ParseAssemblyResult r1 = ParseAssemblyName(assemblyIdentity1, out name1);
            ParseAssemblyResult r2 = ParseAssemblyName(assemblyIdentity2, out name2);

            Version version1;
            if (unified1)
            {
                if (name1.Name == null || !ParseVersion(name1.Version, out version1) || version1 == null || version1.Revision == -1
                    || name1.Culture == null || name1.PublicKeyToken == null || name1.PublicKeyToken.Length < 2)
                {
                    result = AssemblyComparisonResult.NonEquivalent;
                    throw new ArgumentException();
                }
            }

            Version version2 = null;
            if (!ParseVersion(name2.Version, out version2) || version2 == null || version2.Revision == -1
                || name2.Culture == null || name2.PublicKeyToken == null || name2.PublicKeyToken.Length < 2)
            {
                result = AssemblyComparisonResult.NonEquivalent;
                throw new ArgumentException();
            }

            if (name2.Name != null && name2.Name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase))
            {
                if (name1.Name != null && name1.Name.Equals(name2.Name, StringComparison.OrdinalIgnoreCase))
                {
                    result = AssemblyComparisonResult.EquivalentFullMatch;
                    return true;
                }
                else
                {
                    result = AssemblyComparisonResult.NonEquivalent;
                    return false;
                }
            }

            if (r1 != ParseAssemblyResult.OK)
            {
                result = AssemblyComparisonResult.NonEquivalent;
                switch (r1)
                {
                    case ParseAssemblyResult.DuplicateKey:
                        throw new System.IO.FileLoadException();
                    case ParseAssemblyResult.GenericError:
                    default:
                        throw new ArgumentException();
                }
            }

            if (r2 != ParseAssemblyResult.OK)
            {
                result = AssemblyComparisonResult.NonEquivalent;
                switch (r2)
                {
                    case ParseAssemblyResult.DuplicateKey:
                        throw new System.IO.FileLoadException();
                    case ParseAssemblyResult.GenericError:
                    default:
                        throw new ArgumentException();
                }
            }

            if (!ParseVersion(name1.Version, out version1))
            {
                result = AssemblyComparisonResult.NonEquivalent;
                throw new ArgumentException();
            }

            bool partial = IsPartial(name1, version1);

            if (partial && name1.Retargetable.HasValue)
            {
                result = AssemblyComparisonResult.NonEquivalent;
                throw new System.IO.FileLoadException();
            }
            if ((partial && unified1) || IsPartial(name2, version2))
            {
                result = AssemblyComparisonResult.NonEquivalent;
                throw new ArgumentException();
            }
            if (!name1.Name.Equals(name2.Name, StringComparison.OrdinalIgnoreCase))
            {
                result = AssemblyComparisonResult.NonEquivalent;
                return false;
            }
            if (partial && name1.Culture == null)
            {
            }
            else if (!name1.Culture.Equals(name2.Culture, StringComparison.OrdinalIgnoreCase))
            {
                result = AssemblyComparisonResult.NonEquivalent;
                return false;
            }

            if (!name1.Retargetable.GetValueOrDefault() && name2.Retargetable.GetValueOrDefault())
            {
                result = AssemblyComparisonResult.NonEquivalent;
                return false;
            }

            // HACK handle the case "System.Net, Version=4.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e, Retargetable=Yes"
            // compared with "System.Net, Version=4.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e, Retargetable=No"
            if (name1.PublicKeyToken == name2.PublicKeyToken
                && version1 != null
                && name1.Retargetable.GetValueOrDefault()
                && !name2.Retargetable.GetValueOrDefault()
                && GetRemappedPublicKeyToken(ref name1, version1) != null)
            {
                name1.Retargetable = false;
            }

            string remappedPublicKeyToken1 = null;
            string remappedPublicKeyToken2 = null;
            if (version1 != null && (remappedPublicKeyToken1 = GetRemappedPublicKeyToken(ref name1, version1)) != null)
            {
                name1.PublicKeyToken = remappedPublicKeyToken1;
                version1 = FrameworkVersion;
            }
            if ((remappedPublicKeyToken2 = GetRemappedPublicKeyToken(ref name2, version2)) != null)
            {
                name2.PublicKeyToken = remappedPublicKeyToken2;
                version2 = FrameworkVersion;
            }
            if (name1.Retargetable.GetValueOrDefault())
            {
                if (name2.Retargetable.GetValueOrDefault())
                {
                    if (remappedPublicKeyToken1 != null ^ remappedPublicKeyToken2 != null)
                    {
                        result = AssemblyComparisonResult.NonEquivalent;
                        return false;
                    }
                }
                else if (remappedPublicKeyToken1 == null || remappedPublicKeyToken2 != null)
                {
                    result = AssemblyComparisonResult.Unknown;
                    return false;
                }
            }

            bool fxUnified = false;

            // build and revision numbers are ignored
            bool fxVersionMatch = version1.Major == version2.Major;
            // && version1.Minor == version2.Minor;
            if (IsFrameworkAssembly(name1))
            {
                fxUnified |= !fxVersionMatch;
                version1 = FrameworkVersion;
            }
            // && version2 < FrameworkVersionNext
            if (IsFrameworkAssembly(name2))
            {
                fxUnified |= !fxVersionMatch;
                version2 = FrameworkVersion;
            }

            if (IsStrongNamed(name2))
            {
                if (name1.PublicKeyToken != null && name1.PublicKeyToken != name2.PublicKeyToken)
                {
                    result = AssemblyComparisonResult.NonEquivalent;
                    return false;
                }
                else if (version1 == null)
                {
                    result = AssemblyComparisonResult.EquivalentPartialMatch;
                    return true;
                }
                else if (version1.Revision == -1 || version2.Revision == -1)
                {
                    result = AssemblyComparisonResult.NonEquivalent;
                    throw new ArgumentException();
                }
                else if (version1 < version2)
                {
                    if (unified2)
                    {
                        result = partial ? AssemblyComparisonResult.EquivalentPartialUnified : AssemblyComparisonResult.EquivalentUnified;
                        return true;
                    }
                    else
                    {
                        result = partial ? AssemblyComparisonResult.NonEquivalentPartialVersion : AssemblyComparisonResult.NonEquivalentVersion;
                        return false;
                    }
                }
                else if (version1 > version2)
                {
                    if (unified1)
                    {
                        result = partial ? AssemblyComparisonResult.EquivalentPartialUnified : AssemblyComparisonResult.EquivalentUnified;
                        return true;
                    }
                    else
                    {
                        result = partial ? AssemblyComparisonResult.NonEquivalentPartialVersion : AssemblyComparisonResult.NonEquivalentVersion;
                        return false;
                    }
                }
                else if (fxUnified || version1 != version2)
                {
                    result = partial ? AssemblyComparisonResult.EquivalentPartialFXUnified : AssemblyComparisonResult.EquivalentFXUnified;
                    return true;
                }
                else
                {
                    result = partial ? AssemblyComparisonResult.EquivalentPartialMatch : AssemblyComparisonResult.EquivalentFullMatch;
                    return true;
                }
            }
            else if (IsStrongNamed(name1))
            {
                result = AssemblyComparisonResult.NonEquivalent;
                return false;
            }
            else
            {
                result = partial ? AssemblyComparisonResult.EquivalentPartialWeakNamed : AssemblyComparisonResult.EquivalentWeakNamed;
                return true;
            }
        }

        static bool IsFrameworkAssembly(ParsedAssemblyName name)
        {
            // Framework assemblies use different unification rules, so when
            // a new framework is released the new assemblies need to be added.
            switch (name.Name)
            {
                case "mscorlib":
                case "System":
                case "System.ComponentModel.Composition":
                case "System.Core":
                case "System.Data":
                case "System.Data.DataSetExtensions":
                case "System.Data.Linq":
                case "System.Data.OracleClient":
                case "System.Data.Services":
                case "System.Data.Services.Client":
                case "System.IdentityModel":
                case "System.IdentityModel.Selectors":
                case "System.IO.Compression":
                case "System.IO.Compression.Brotli":
                case "System.IO.Compression.FileSystem":
                case "System.IO.Compression.ZipFile":
                case "System.Numerics":
                case "System.Runtime.Remoting":
                case "System.Runtime.Serialization":
                case "System.ServiceModel":
                case "System.Transactions":
                case "System.Windows.Forms":
                case "System.Xml":
                case "System.Xml.Linq":
                case "System.Xml.Serialization":
                    return name.PublicKeyToken == PublicKeyTokenEcma;

                case "System.Reflection.Context":
                case "System.Runtime.WindowsRuntime":
                case "System.Runtime.WindowsRuntime.UI.Xaml":
                    return name.PublicKeyToken == PublicKeyTokenEcma || name.PublicKeyToken == PublicKeyTokenMicrosoft;

                case "Microsoft.CSharp":
                case "Microsoft.VisualBasic":
                case "Microsoft.VisualBasic.Core":
                case "Microsoft.Win32.Primitives":
                case "Microsoft.Win32.Registry":
                case "Microsoft.Win32.Registry.AccessControl":
                case "System.AppContext":
                case "System.Collections":
                case "System.Collections.Concurrent":
                case "System.Collections.Immutable":
                case "System.Collections.NonGeneric":
                case "System.Collections.Specialized":
                case "System.ComponentModel":
                case "System.ComponentModel.Annotations":
                case "System.ComponentModel.EventBasedAsync":
                case "System.ComponentModel.Primitives":
                case "System.ComponentModel.TypeConverter":
                case "System.Configuration":
                case "System.Configuration.Install":
                case "System.Console":
                case "System.Data.Common":
                case "System.Data.SqlClient":
                case "System.Design":
                case "System.Diagnostics.Debug":
                case "System.Diagnostics.Contracts":
                case "System.Diagnostics.FileVersionInfo":
                case "System.Diagnostics.Process":
                case "System.Diagnostics.StackTrace":
                case "System.Diagnostics.TextWriterTraceListener":
                case "System.Diagnostics.Tools":
                case "System.Diagnostics.TraceSource":
                case "System.Diagnostics.Tracing":
                case "System.DirectoryServices":
                case "System.Drawing":
                case "System.Drawing.Design":
                case "System.Drawing.Primitives":
                case "System.Dynamic.Runtime":
                case "System.EnterpriseServices":
                case "System.Globalization":
                case "System.Globalization.Calendars":
                case "System.Globalization.Extensions":
                case "System.IO":
                case "System.IO.FileSystem":
                case "System.IO.FileSystem.AccessControl":
                case "System.IO.FileSystem.DriveInfo":
                case "System.IO.FileSystem.Primitives":
                case "System.IO.FileSystem.Watcher":
                case "System.IO.IsolatedStorage":
                case "System.IO.MemoryMappedFiles":
                case "System.IO.Packaging":
                case "System.IO.Pipes":
                case "System.IO.UnmanagedMemoryStream":
                case "System.Linq":
                case "System.Linq.Expressions":
                case "System.Linq.Parallel":
                case "System.Linq.Queryable":
                case "System.Management":
                case "System.Messaging":
                case "System.Net":
                case "System.Net.Http":
                case "System.Net.Http.Rtc":
                case "System.Net.Http.WinHttpHandler":
                case "System.Net.NameResolution":
                case "System.Net.NetworkInformation":
                case "System.Net.Ping":
                case "System.Net.Primitives":
                case "System.Net.Requests":
                case "System.Net.Security":
                case "System.Net.Sockets":
                case "System.Net.WebHeaderCollection":
                case "System.Net.WebSockets":
                case "System.Net.WebSockets.Client":
                case "System.Numerics.Vectors":
                case "System.ObjectModel":
                case "System.Reflection":
                case "System.Reflection.DispatchProxy":
                case "System.Reflection.Emit":
                case "System.Reflection.Emit.ILGeneration":
                case "System.Reflection.Emit.Lightweight":
                case "System.Reflection.Extensions":
                case "System.Reflection.Metadata":
                case "System.Reflection.Primitives":
                case "System.Reflection.TypeExtensions":
                case "System.Resources.Reader":
                case "System.Resources.ResourceManager":
                case "System.Resources.Writer":
                case "System.Runtime":
                case "System.Runtime.CompilerServices.Unsafe":
                case "System.Runtime.CompilerServices.VisualC":
                case "System.Runtime.Extensions":
                case "System.Runtime.Handles":
                case "System.Runtime.InteropServices":
                case "System.Runtime.InteropServices.PInvoke":
                case "System.Runtime.InteropServices.RuntimeInformation":
                case "System.Runtime.InteropServices.WindowsRuntime":
                case "System.Runtime.Loader":
                case "System.Runtime.Numerics":
                case "System.Runtime.Serialization.Formatters.Soap":
                case "System.Runtime.Serialization.Json":
                case "System.Runtime.Serialization.Primitives":
                case "System.Runtime.Serialization.Xml":
                case "System.Security":
                case "System.Security.AccessControl":
                case "System.Security.Claims":
                case "System.Security.Cryptography.Algorithms":
                case "System.Security.Cryptography.Cng":
                case "System.Security.Cryptography.Csp":
                case "System.Security.Cryptography.Encoding":
                case "System.Security.Cryptography.OpenSsl":
                case "System.Security.Cryptography.Pkcs":
                case "System.Security.Cryptography.Primitives":
                case "System.Security.Cryptography.ProtectedData":
                case "System.Security.Cryptography.X509Certificates":
                case "System.Security.Principal":
                case "System.Security.Principal.Windows":
                case "System.Security.SecureString":
                case "System.ServiceModel.Duplex":
                case "System.ServiceModel.Http":
                case "System.ServiceModel.NetTcp":
                case "System.ServiceModel.Primitives":
                case "System.ServiceModel.Security":
                case "System.ServiceProcess":
                case "System.ServiceProcess.ServiceController":
                case "System.Text.Encoding":
                case "System.Text.Encoding.CodePages":
                case "System.Text.Encoding.Extensions":
                case "System.Text.RegularExpressions":
                case "System.Threading":
                case "System.Threading.AccessControl":
                case "System.Threading.Overlapped":
                case "System.Threading.Tasks":
                case "System.Threading.Tasks.Dataflow":
                case "System.Threading.Tasks.Parallel":
                case "System.Threading.Thread":
                case "System.Threading.ThreadPool":
                case "System.Threading.Timer":
                case "System.Web":
                case "System.Web.Mobile":
                case "System.Web.Services":
                case "System.Windows":
                case "System.Xml.ReaderWriter":
                case "System.Xml.XDocument":
                case "System.Xml.XmlDocument":
                case "System.Xml.XmlSerializer":
                case "System.Xml.XPath":
                case "System.Xml.XPath.XDocument":
                case "System.Xml.XPath.XmlDocument":
                    return name.PublicKeyToken == PublicKeyTokenMicrosoft;

                case "System.ComponentModel.DataAnnotations":
                case "System.ServiceModel.Web":
                case "System.Web.Abstractions":
                case "System.Web.Extensions":
                case "System.Web.Extensions.Design":
                case "System.Web.DynamicData":
                case "System.Web.Routing":
                case "WindowsBase":
                    return name.PublicKeyToken == PublicKeyTokenWinFX;

                case "netstandard":
                case "System.Buffers":
                case "System.Diagnostics.DiagnosticSource":
                case "System.Formats.Asn1":
                case "System.Memory":
                case "System.Net.HttpListener":
                case "System.Net.Http.Json":
                case "System.Net.Mail":
                case "System.Net.ServicePoint":
                case "System.Net.WebClient":
                case "System.Net.WebProxy":
                case "System.Runtime.Intrinsics":
                case "System.Runtime.Serialization.Formatters":
                case "System.Text.Encodings.Web":
                case "System.Text.Json":
                case "System.Threading.Channels":
                case "System.Threading.Tasks.Extensions":
                case "System.Transactions.Local":
                case "System.ValueTuple":
                case "System.Web.HttpUtility":
                    return name.PublicKeyToken == PublicKeyTokenNetStandard;
            }

            return false;
        }

        static string GetRemappedPublicKeyToken(ref ParsedAssemblyName name, Version version)
        {
            if (name.Retargetable.GetValueOrDefault() && version < SilverlightVersion)
            {
                return null;
            }
            if (name.PublicKeyToken == "ddd0da4d3e678217" && name.Name == "System.ComponentModel.DataAnnotations" && name.Retargetable.GetValueOrDefault())
            {
                return PublicKeyTokenWinFX;
            }
            if (SilverlightVersionMinimum <= version && version <= SilverlightVersionMaximum)
            {
                switch (name.PublicKeyToken)
                {
                    case PublicKeyTokenSilverlight:
                        switch (name.Name)
                        {
                            case "System":
                            case "System.Core":
                                return PublicKeyTokenEcma;
                        }
                        if (name.Retargetable.GetValueOrDefault())
                        {
                            switch (name.Name)
                            {
                                case "System.Runtime.Serialization":
                                case "System.Xml":
                                    return PublicKeyTokenEcma;
                                case "System.Net":
                                case "System.Windows":
                                    return PublicKeyTokenMicrosoft;
                                case "System.ServiceModel.Web":
                                    return PublicKeyTokenWinFX;
                            }
                        }
                        break;
                    case PublicKeyTokenWinFX:
                        switch (name.Name)
                        {
                            case "System.ComponentModel.Composition":
                                return PublicKeyTokenEcma;
                        }
                        if (name.Retargetable.GetValueOrDefault())
                        {
                            switch (name.Name)
                            {
                                case "Microsoft.CSharp":
                                    return PublicKeyTokenMicrosoft;
                                case "System.Numerics":
                                case "System.ServiceModel":
                                case "System.Xml.Serialization":
                                case "System.Xml.Linq":
                                    return PublicKeyTokenEcma;
                            }
                        }
                        break;
                }
            }
            return null;
        }

        internal static ParseAssemblyResult ParseAssemblySimpleName(string fullName, out int pos, out string simpleName)
        {
            pos = 0;
            if (!TryParse(fullName, ref pos, out simpleName) || simpleName.Length == 0)
            {
                return ParseAssemblyResult.GenericError;
            }
            if (pos == fullName.Length && fullName[fullName.Length - 1] == ',')
            {
                return ParseAssemblyResult.GenericError;
            }
            return ParseAssemblyResult.OK;
        }

        private static bool TryParse(string fullName, ref int pos, out string value)
        {
            value = null;
            StringBuilder sb = new StringBuilder();
            while (pos < fullName.Length && char.IsWhiteSpace(fullName[pos]))
            {
                pos++;
            }
            int quote = -1;
            if (pos < fullName.Length && (fullName[pos] == '"' || fullName[pos] == '\''))
            {
                quote = fullName[pos++];
            }
            for (; pos < fullName.Length; pos++)
            {
                char ch = fullName[pos];
                if (ch == '\\')
                {
                    if (++pos == fullName.Length)
                    {
                        return false;
                    }
                    ch = fullName[pos];
                    if (ch == '\\')
                    {
                        return false;
                    }
                }
                else if (ch == quote)
                {
                    for (pos++; pos != fullName.Length; pos++)
                    {
                        ch = fullName[pos];
                        if (ch == ',' || ch == '=')
                        {
                            break;
                        }
                        if (!char.IsWhiteSpace(ch))
                        {
                            return false;
                        }
                    }
                    break;
                }
                else if (quote == -1 && (ch == '"' || ch == '\''))
                {
                    return false;
                }
                else if (quote == -1 && (ch == ',' || ch == '='))
                {
                    break;
                }
                sb.Append(ch);
            }
            value = sb.ToString().Trim();
            return value.Length != 0 || quote != -1;
        }

        private static bool TryConsume(string fullName, char ch, ref int pos)
        {
            if (pos < fullName.Length && fullName[pos] == ch)
            {
                pos++;
                return true;
            }
            return false;
        }

        private static bool TryParseAssemblyAttribute(string fullName, ref int pos, ref string key, ref string value)
        {
            return TryConsume(fullName, ',', ref pos)
                && TryParse(fullName, ref pos, out key)
                && TryConsume(fullName, '=', ref pos)
                && TryParse(fullName, ref pos, out value);
        }

        internal static ParseAssemblyResult ParseAssemblyName(string fullName, out ParsedAssemblyName parsedName)
        {
            parsedName = new ParsedAssemblyName();
            int pos;
            ParseAssemblyResult res = ParseAssemblySimpleName(fullName, out pos, out parsedName.Name);
            if (res != ParseAssemblyResult.OK)
            {
                return res;
            }
            else
            {
                const int ERROR_SXS_IDENTITIES_DIFFERENT = unchecked((int)0x80073716);
                System.Collections.Generic.Dictionary<string, string> unknownAttributes = null;
                bool hasProcessorArchitecture = false;
                bool hasContentType = false;
                bool hasPublicKeyToken = false;
                string publicKeyToken;
                while (pos != fullName.Length)
                {
                    string key = null;
                    string value = null;
                    if (!TryParseAssemblyAttribute(fullName, ref pos, ref key, ref value))
                    {
                        return ParseAssemblyResult.GenericError;
                    }
                    key = key.ToLowerInvariant();
                    switch (key)
                    {
                        case "version":
                            if (parsedName.Version != null)
                            {
                                return ParseAssemblyResult.DuplicateKey;
                            }
                            parsedName.Version = value;
                            break;
                        case "culture":
                            if (parsedName.Culture != null)
                            {
                                return ParseAssemblyResult.DuplicateKey;
                            }
                            if (!ParseCulture(value, out parsedName.Culture))
                            {
                                return ParseAssemblyResult.GenericError;
                            }
                            break;
                        case "publickeytoken":
                            if (hasPublicKeyToken)
                            {
                                return ParseAssemblyResult.DuplicateKey;
                            }
                            if (!ParsePublicKeyToken(value, out publicKeyToken))
                            {
                                return ParseAssemblyResult.GenericError;
                            }
                            if (parsedName.HasPublicKey && parsedName.PublicKeyToken != publicKeyToken)
                            {
                                Marshal.ThrowExceptionForHR(ERROR_SXS_IDENTITIES_DIFFERENT);
                            }
                            parsedName.PublicKeyToken = publicKeyToken;
                            hasPublicKeyToken = true;
                            break;
                        case "publickey":
                            if (parsedName.HasPublicKey)
                            {
                                return ParseAssemblyResult.DuplicateKey;
                            }
                            if (!ParsePublicKey(value, out publicKeyToken))
                            {
                                return ParseAssemblyResult.GenericError;
                            }
                            if (hasPublicKeyToken && parsedName.PublicKeyToken != publicKeyToken)
                            {
                                Marshal.ThrowExceptionForHR(ERROR_SXS_IDENTITIES_DIFFERENT);
                            }
                            parsedName.PublicKeyToken = publicKeyToken;
                            parsedName.HasPublicKey = true;
                            break;
                        case "retargetable":
                            if (parsedName.Retargetable.HasValue)
                            {
                                return ParseAssemblyResult.DuplicateKey;
                            }
                            switch (value.ToLowerInvariant())
                            {
                                case "yes":
                                    parsedName.Retargetable = true;
                                    break;
                                case "no":
                                    parsedName.Retargetable = false;
                                    break;
                                default:
                                    return ParseAssemblyResult.GenericError;
                            }
                            break;
                        case "processorarchitecture":
                            if (hasProcessorArchitecture)
                            {
                                return ParseAssemblyResult.DuplicateKey;
                            }
                            hasProcessorArchitecture = true;
                            switch (value.ToLowerInvariant())
                            {
                                case "none":
                                    parsedName.ProcessorArchitecture = ProcessorArchitecture.None;
                                    break;
                                case "msil":
                                    parsedName.ProcessorArchitecture = ProcessorArchitecture.MSIL;
                                    break;
                                case "x86":
                                    parsedName.ProcessorArchitecture = ProcessorArchitecture.X86;
                                    break;
                                case "ia64":
                                    parsedName.ProcessorArchitecture = ProcessorArchitecture.IA64;
                                    break;
                                case "amd64":
                                    parsedName.ProcessorArchitecture = ProcessorArchitecture.Amd64;
                                    break;
                                case "arm":
                                    parsedName.ProcessorArchitecture = ProcessorArchitecture.Arm;
                                    break;
                                default:
                                    return ParseAssemblyResult.GenericError;
                            }
                            break;
                        case "contenttype":
                            if (hasContentType)
                            {
                                return ParseAssemblyResult.DuplicateKey;
                            }
                            hasContentType = true;
                            if (!value.Equals("windowsruntime", StringComparison.OrdinalIgnoreCase))
                            {
                                return ParseAssemblyResult.GenericError;
                            }
                            parsedName.WindowsRuntime = true;
                            break;
                        default:
                            if (key.Length == 0)
                            {
                                return ParseAssemblyResult.GenericError;
                            }
                            if (unknownAttributes == null)
                            {
                                unknownAttributes = new System.Collections.Generic.Dictionary<string, string>();
                            }
                            if (unknownAttributes.ContainsKey(key))
                            {
                                return ParseAssemblyResult.DuplicateKey;
                            }
                            unknownAttributes.Add(key, null);
                            break;
                    }
                }
                return ParseAssemblyResult.OK;
            }
        }

        private static bool ParseVersion(string str, out Version version)
        {
            if (str == null)
            {
                version = null;
                return true;
            }
            string[] parts = str.Split('.');
            if (parts.Length < 2 || parts.Length > 4)
            {
                version = null;
                ushort dummy;
                // if the version consists of a single integer, it is invalid, but not invalid enough to fail the parse of the whole assembly name
                return parts.Length == 1 && ushort.TryParse(parts[0], System.Globalization.NumberStyles.Integer, null, out dummy);
            }
            if (parts[0] == "" || parts[1] == "")
            {
                // this is a strange scenario, the version is invalid, but not invalid enough to fail the parse of the whole assembly name
                version = null;
                return true;
            }
            ushort major, minor, build = 65535, revision = 65535;
            if (ushort.TryParse(parts[0], System.Globalization.NumberStyles.Integer, null, out major)
                && ushort.TryParse(parts[1], System.Globalization.NumberStyles.Integer, null, out minor)
                && (parts.Length <= 2 || parts[2] == "" || ushort.TryParse(parts[2], System.Globalization.NumberStyles.Integer, null, out build))
                && (parts.Length <= 3 || parts[3] == "" || (parts[2] != "" && ushort.TryParse(parts[3], System.Globalization.NumberStyles.Integer, null, out revision))))
            {
                if (parts.Length == 4 && parts[3] != "" && parts[2] != "")
                {
                    version = new Version(major, minor, build, revision);
                }
                else if (parts.Length == 3 && parts[2] != "")
                {
                    version = new Version(major, minor, build);
                }
                else
                {
                    version = new Version(major, minor);
                }
                return true;
            }
            version = null;
            return false;
        }

        private static bool ParseCulture(string str, out string culture)
        {
            if (str == null)
            {
                culture = null;
                return false;
            }
            culture = str;
            return true;
        }

        private static bool ParsePublicKeyToken(string str, out string publicKeyToken)
        {
            if (str == null)
            {
                publicKeyToken = null;
                return false;
            }
            publicKeyToken = str.ToLowerInvariant();
            return true;
        }

        private static bool ParsePublicKey(string str, out string publicKeyToken)
        {
            if (str == null)
            {
                publicKeyToken = null;
                return false;
            }
            publicKeyToken = AssemblyName.ComputePublicKeyToken(str);
            return true;
        }

        private static bool IsPartial(ParsedAssemblyName name, Version version)
        {
            return version == null || name.Culture == null || name.PublicKeyToken == null;
        }

        private static bool IsStrongNamed(ParsedAssemblyName name)
        {
            return name.PublicKeyToken != null && name.PublicKeyToken != "null";
        }
    }
}
