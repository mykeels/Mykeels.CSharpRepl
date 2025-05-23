﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

//Modified copy of https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/InternalUtilities/DocumentationCommentXmlNames.cs

using System;

namespace CSharpRepl.Services.Roslyn;

/// <summary>
/// Names of well-known XML attributes and elements.
/// </summary>
internal static class DocumentationCommentXmlNames
{
    public const string CElementName = "c";
    public const string CodeElementName = "code";
    public const string CompletionListElementName = "completionlist";
    public const string DescriptionElementName = "description";
    public const string ExampleElementName = "example";
    public const string ExceptionElementName = "exception";
    public const string IncludeElementName = "include";
    public const string InheritdocElementName = "inheritdoc";
    public const string ItemElementName = "item";
    public const string ListElementName = "list";
    public const string ListHeaderElementName = "listheader";
    public const string ParaElementName = "para";
    public const string ParameterElementName = "param";
    public const string ParameterReferenceElementName = "paramref";
    public const string PermissionElementName = "permission";
    public const string PlaceholderElementName = "placeholder";
    public const string PreliminaryElementName = "preliminary";
    public const string RemarksElementName = "remarks";
    public const string ReturnsElementName = "returns";
    public const string SeeElementName = "see";
    public const string SeeAlsoElementName = "seealso";
    public const string SummaryElementName = "summary";
    public const string TermElementName = "term";
    public const string ThreadSafetyElementName = "threadsafety";
    public const string TypeParameterElementName = "typeparam";
    public const string TypeParameterReferenceElementName = "typeparamref";
    public const string ValueElementName = "value";

    public const string CrefAttributeName = "cref";
    public const string HrefAttributeName = "href";
    public const string FileAttributeName = "file";
    public const string InstanceAttributeName = "instance";
    public const string LangwordAttributeName = "langword";
    public const string NameAttributeName = "name";
    public const string PathAttributeName = "path";
    public const string StaticAttributeName = "static";
    public const string TypeAttributeName = "type";

    public static bool ElementEquals(string name1, string name2, bool fromVb = false)
    {
        return string.Equals(
            name1,
            name2,
            fromVb ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase
        );
    }

    public static bool AttributeEquals(string name1, string name2)
    {
        return string.Equals(name1, name2, StringComparison.Ordinal);
    }

    public static new bool Equals(object left, object right)
    {
        return object.Equals(left, right);
    }
}
