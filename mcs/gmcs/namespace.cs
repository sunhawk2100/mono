//
// namespace.cs: Tracks namespaces
//
// Author:
//   Miguel de Icaza (miguel@ximian.com)
//
// (C) 2001 Ximian, Inc.
//
using System;
using System.Collections;

namespace Mono.CSharp {

	/// <summary>
	///   Keeps track of the namespaces defined in the C# code.
	///
	///   This is an Expression to allow it to be referenced in the
	///   compiler parse/intermediate tree during name resolution.
	/// </summary>
	public class Namespace : FullNamedExpression, IAlias {
		static ArrayList all_namespaces;
		static Hashtable namespaces_map;
		
		Namespace parent;
		string fullname;
		ArrayList entries;
		Hashtable namespaces;
		Hashtable defined_names;
		Hashtable cached_types;

		public readonly MemberName MemberName;

		public static Namespace Root;

		static Namespace ()
		{
			Reset ();
		}

		public static void Reset ()
		{
			all_namespaces = new ArrayList ();
			namespaces_map = new Hashtable ();

			Root = new Namespace (null, "");
		}

		/// <summary>
		///   Constructor Takes the current namespace and the
		///   name.  This is bootstrapped with parent == null
		///   and name = ""
		/// </summary>
		public Namespace (Namespace parent, string name)
		{
			// Expression members.
			this.eclass = ExprClass.Namespace;
			this.Type = null;
			this.loc = Location.Null;

			this.parent = parent;

			string pname = parent != null ? parent.Name : "";
				
			if (pname == "")
				fullname = name;
			else
				fullname = parent.Name + "." + name;

			if (parent != null && parent.MemberName != MemberName.Null)
				MemberName = new MemberName (parent.MemberName, name);
			else if (name == "")
				MemberName = MemberName.Null;
			else
				MemberName = new MemberName (name);

			entries = new ArrayList ();
			namespaces = new Hashtable ();
			defined_names = new Hashtable ();
			cached_types = new Hashtable ();

			all_namespaces.Add (this);
			if (namespaces_map.Contains (fullname))
				return;
			namespaces_map [fullname] = true;
		}

		public override Expression DoResolve (EmitContext ec)
		{
			return this;
		}

		public override void Emit (EmitContext ec)
		{
			throw new InternalErrorException ("Expression tree referenced namespace " + fullname + " during Emit ()");
		}

		public static bool IsNamespace (string name)
		{
			return namespaces_map [name] != null;
		}

		public override string GetSignatureForError ()
		{
			return Name.Length == 0 ? "::global" : Name;
		}
		
		public Namespace GetNamespace (string name, bool create)
		{
			int pos = name.IndexOf ('.');

			Namespace ns;
			string first;
			if (pos >= 0)
				first = name.Substring (0, pos);
			else
				first = name;

			ns = (Namespace) namespaces [first];
			if (ns == null) {
				if (!create)
					return null;

				ns = new Namespace (this, first);
				namespaces.Add (first, ns);
			}

			if (pos >= 0)
				ns = ns.GetNamespace (name.Substring (pos + 1), create);

			return ns;
		}

		public static Namespace LookupNamespace (string name, bool create)
		{
			return Root.GetNamespace (name, create);
		}

		public FullNamedExpression Lookup (DeclSpace ds, string name, Location loc)
		{
			Namespace ns = GetNamespace (name, false);
			if (ns != null)
				return ns;

			TypeExpr te;
			if (cached_types.Contains (name)) {
				te = (TypeExpr) cached_types [name];
			} else {
				Type t;
				DeclSpace tdecl = defined_names [name] as DeclSpace;
				if (tdecl != null) {
					//
					// Note that this is not:
					//
					//   t = tdecl.DefineType ()
					//
					// This is to make it somewhat more useful when a DefineType
					// fails due to problems in nested types (more useful in the sense
					// of fewer misleading error messages)
					//
					tdecl.DefineType ();
					t = tdecl.TypeBuilder;
				} else {
					string lookup = this == Namespace.Root ? name : fullname + "." + name;
					t = TypeManager.LookupTypeReflection (lookup);
				}
				te = t == null ? null : new TypeExpression (t, Location.Null);
				cached_types [name] = te;
			}

			if (te != null && ds != null && !ds.CheckAccessLevel (te.Type))
				return null;

			return te;
		}

		public void AddNamespaceEntry (NamespaceEntry entry)
		{
			entries.Add (entry);
		}

		public void DefineName (string name, IAlias o)
		{
			defined_names.Add (name, o);
		}

		static public ArrayList UserDefinedNamespaces {
			get {
				return all_namespaces;
			}
		}

		/// <summary>
		///   The qualified name of the current namespace
		/// </summary>
		public string Name {
			get {
				return fullname;
			}
		}

		public override string FullName {
			get {
				return fullname;
			}
		}

		/// <summary>
		///   The parent of this namespace, used by the parser to "Pop"
		///   the current namespace declaration
		/// </summary>
		public Namespace Parent {
			get {
				return parent;
			}
		}

		public static void DefineNamespaces (SymbolWriter symwriter)
		{
			foreach (Namespace ns in all_namespaces) {
				foreach (NamespaceEntry entry in ns.entries)
					entry.DefineNamespace (symwriter);
			}
		}

		/// <summary>
		///   Used to validate that all the using clauses are correct
		///   after we are finished parsing all the files.  
		/// </summary>
		public static void VerifyUsing ()
		{
			foreach (Namespace ns in all_namespaces) {
				foreach (NamespaceEntry entry in ns.entries)
					entry.VerifyUsing ();
			}
		}

		public override string ToString ()
		{
			if (this == Root)
				return "Namespace (<root>)";
			else
				return String.Format ("Namespace ({0})", Name);
		}

		bool IAlias.IsType {
			get { return false; }
		}

		TypeExpr IAlias.ResolveAsType (EmitContext ec)
		{
			throw new InvalidOperationException ();
		}
	}

	public class NamespaceEntry
	{
		Namespace ns;
		NamespaceEntry parent, implicit_parent;
		SourceFile file;
		int symfile_id;
		Hashtable aliases;
		ArrayList using_clauses;
		public bool DeclarationFound = false;

		//
		// This class holds the location where a using definition is
		// done, and whether it has been used by the program or not.
		//
		// We use this to flag using clauses for namespaces that do not
		// exist.
		//
		public class UsingEntry {
			public MemberName Name;
			public Expression Expr;
			public readonly NamespaceEntry NamespaceEntry;
			public readonly Location Location;
			
			public UsingEntry (NamespaceEntry entry, MemberName name, Location loc)
			{
				Name = name;
				Expr = name.GetTypeExpression (loc);
				NamespaceEntry = entry;
				Location = loc;
			}

			internal FullNamedExpression resolved;

			public Namespace Resolve ()
			{
				if (resolved != null)
					return resolved as Namespace;

				DeclSpace root = RootContext.Tree.Types;
				root.NamespaceEntry = NamespaceEntry;
				resolved = Expr.ResolveAsTypeStep (root.EmitContext);
				root.NamespaceEntry = null;

				return resolved as Namespace;
			}
		}

		public class AliasEntry {
			public readonly string Name;
			public readonly Expression Alias;
			public readonly NamespaceEntry NamespaceEntry;
			public readonly Location Location;
			
			public AliasEntry (NamespaceEntry entry, string name, MemberName alias, Location loc)
			{
				Name = name;
				Alias = alias.GetTypeExpression (loc);
				NamespaceEntry = entry;
				Location = loc;
			}

			FullNamedExpression resolved;

			public FullNamedExpression Resolve ()
			{
				if (resolved != null)
					return resolved;

				DeclSpace root = RootContext.Tree.Types;
				root.NamespaceEntry = NamespaceEntry;
				resolved = Alias.ResolveAsTypeStep (root.EmitContext);
				root.NamespaceEntry = null;

				return resolved;
			}
		}

		public NamespaceEntry (NamespaceEntry parent, SourceFile file, string name, Location loc)
		{
			this.parent = parent;
			this.file = file;
			this.IsImplicit = false;
			this.ID = ++next_id;

			if (parent != null)
				ns = parent.NS.GetNamespace (name, true);
			else if (name != null)
				ns = Namespace.LookupNamespace (name, true);
			else
				ns = Namespace.Root;
			ns.AddNamespaceEntry (this);
		}


		private NamespaceEntry (NamespaceEntry parent, SourceFile file, Namespace ns)
		{
			this.parent = parent;
			this.file = file;
			this.IsImplicit = true;
			this.ID = ++next_id;
			this.ns = ns;
		}

		//
		// According to section 16.3.1 (using-alias-directive), the namespace-or-type-name is
		// resolved as if the immediately containing namespace body has no using-directives.
		//
		// Section 16.3.2 says that the same rule is applied when resolving the namespace-name
		// in the using-namespace-directive.
		//
		// To implement these rules, the expressions in the using directives are resolved using 
		// the "doppelganger" (ghostly bodiless duplicate).
		//
		NamespaceEntry doppelganger;
		NamespaceEntry Doppelganger {
			get {
				if (!IsImplicit && doppelganger == null)
					doppelganger = new NamespaceEntry (ImplicitParent, file, ns);
				return doppelganger;
			}
		}

		static int next_id = 0;
		public readonly int ID;
		public readonly bool IsImplicit;

		public Namespace NS {
			get {
				return ns;
			}
		}

		public NamespaceEntry Parent {
			get {
				return parent;
			}
		}

		public NamespaceEntry ImplicitParent {
			get {
				if (parent == null)
					return null;
				if (implicit_parent == null) {
					implicit_parent = (parent.NS == ns.Parent)
						? parent
						: new NamespaceEntry (parent, file, ns.Parent);
				}
				return implicit_parent;
			}
		}

		/// <summary>
		///   Records a new namespace for resolving name references
		/// </summary>
		public void Using (MemberName name, Location loc)
		{
			if (DeclarationFound){
				Report.Error (1529, loc, "A using clause must precede all other namespace elements except extern alias declarations");
				return;
			}

			if (name.Equals (ns.MemberName))
				return;
			
			if (using_clauses == null)
				using_clauses = new ArrayList ();

			foreach (UsingEntry old_entry in using_clauses) {
				if (name.Equals (old_entry.Name)) {
					if (RootContext.WarningLevel >= 3)
						Report.Warning (105, loc, "The using directive for `{0}' appeared previously in this namespace", name);
						return;
					}
				}


			UsingEntry ue = new UsingEntry (Doppelganger, name, loc);
			using_clauses.Add (ue);
		}

		public void UsingAlias (string name, MemberName alias, Location loc)
		{
			if (DeclarationFound){
				Report.Error (1529, loc, "A using clause must precede all other namespace elements except extern alias declarations");
				return;
			}

			if (aliases == null)
				aliases = new Hashtable ();

			if (aliases.Contains (name)){
				AliasEntry ae = (AliasEntry)aliases [name];
				Report.SymbolRelatedToPreviousError (ae.Location, ae.Name);
				Report.Error (1537, loc, "The using alias `" + name +
					      "' appeared previously in this namespace");
				return;
			}

			aliases [name] = new AliasEntry (Doppelganger, name, alias, loc);
		}

		public FullNamedExpression LookupAlias (string alias)
		{
			AliasEntry entry = null;
			if (aliases != null)
				entry = (AliasEntry) aliases [alias];

			return entry == null ? null : entry.Resolve ();
		}

		static readonly char [] dot_array = { '.' };

		public FullNamedExpression LookupNamespaceOrType (DeclSpace ds, string name, Location loc, bool ignore_cs0104)
		{
			FullNamedExpression resolved = null;
			string rest = null;

			// If name is of the form `N.I', first lookup `N', then search a member `I' in it.
			int pos = name.IndexOf ('.');
			if (pos >= 0) {
				rest = name.Substring (pos + 1);
				name = name.Substring (0, pos);
			}

			for (NamespaceEntry curr_ns = this; curr_ns != null; curr_ns = curr_ns.ImplicitParent) {
				if ((resolved = curr_ns.Lookup (ds, name, loc, ignore_cs0104)) != null)
					break;
			}

			if (resolved == null || rest == null)
				return resolved;

			// Now handle the rest of the the name.
			string [] elements = rest.Split (dot_array);
			int count = elements.Length;
			int i = 0;
			while (i < count && resolved != null && resolved is Namespace) {
				Namespace ns = resolved as Namespace;
				resolved = ns.Lookup (ds, elements [i++], loc);
			}

			if (resolved == null || resolved is Namespace)
				return resolved;

			Type t = ((TypeExpr) resolved).Type;
			
			while (t != null) {
				if (ds != null && !ds.CheckAccessLevel (t))
					break;
				if (i == count)
					return new TypeExpression (t, Location.Null);
				t = TypeManager.GetNestedType (t, elements [i++]);
			}

			return null;
		}

		private FullNamedExpression Lookup (DeclSpace ds, string name, Location loc, bool ignore_cs0104)
		{
			// Precondition: Only simple names (no dots) will be looked up with this function.

			//
			// Check whether it's in the namespace.
			//
			FullNamedExpression o = NS.Lookup (ds, name, loc);
			if (o != null)
				return o;

			if (IsImplicit)
				return null;

			//
			// Check aliases.
			//
			o = LookupAlias (name);
			if (o != null)
				return o;

			//
			// Check using entries.
			//
			FullNamedExpression t = null, match = null;
			foreach (Namespace using_ns in GetUsingTable ()) {
				match = using_ns.Lookup (ds, name, loc);
				if ((match != null) && (match is TypeExpr)) {
					if (t != null) {
						if (!ignore_cs0104)
							DeclSpace.Error_AmbiguousTypeReference (loc, name, t.FullName, match.FullName);
						
						return null;
					} else {
						t = match;
					}
				}
			}

			return t;
		}

		// Our cached computation.
		Namespace [] namespace_using_table;
		public Namespace[] GetUsingTable ()
		{
			if (namespace_using_table != null)
				return namespace_using_table;

			if (using_clauses == null) {
				namespace_using_table = new Namespace [0];
				return namespace_using_table;
			}

			ArrayList list = new ArrayList (using_clauses.Count);

			foreach (UsingEntry ue in using_clauses) {
				Namespace using_ns = ue.Resolve ();
				if (using_ns == null)
					continue;

				list.Add (using_ns);
			}

			namespace_using_table = new Namespace [list.Count];
			list.CopyTo (namespace_using_table, 0);
			return namespace_using_table;
		}

		public void DefineNamespace (SymbolWriter symwriter)
		{
			if (symfile_id != 0)
				return;
			if (parent != null)
				parent.DefineNamespace (symwriter);

			string[] using_list;
			if (using_clauses != null) {
				using_list = new string [using_clauses.Count];
				for (int i = 0; i < using_clauses.Count; i++)
					using_list [i] = ((UsingEntry) using_clauses [i]).Name.ToString ();
			} else {
				using_list = new string [0];
			}

			int parent_id = parent != null ? parent.symfile_id : 0;
			if (file.SourceFileEntry == null)
				return;

			symfile_id = symwriter.DefineNamespace (
				ns.Name, file.SourceFileEntry, using_list, parent_id);
		}

		public int SymbolFileID {
			get {
				return symfile_id;
			}
		}

		static void MsgtryRef (string s)
		{
			Console.WriteLine ("    Try using -r:" + s);
		}
		
		static void MsgtryPkg (string s)
		{
			Console.WriteLine ("    Try using -pkg:" + s);
		}

		public static void Error_NamespaceNotFound (Location loc, string name)
		{
			Report.Error (246, loc, "The type or namespace name `{0}' could not be found. Are you missing a using directive or an assembly reference?",
				name);

			switch (name) {
			case "Gtk": case "GtkSharp":
				MsgtryPkg ("gtk-sharp");
				break;

			case "Gdk": case "GdkSharp":
				MsgtryPkg ("gdk-sharp");
				break;

			case "Glade": case "GladeSharp":
				MsgtryPkg ("glade-sharp");
				break;

			case "System.Drawing":
			case "System.Web.Services":
			case "System.Web":
			case "System.Data":
			case "System.Windows.Forms":
				MsgtryRef (name);
				break;
			}
		}

		/// <summary>
		///   Used to validate that all the using clauses are correct
		///   after we are finished parsing all the files.  
		/// </summary>
		public void VerifyUsing ()
		{
			if (using_clauses != null){
				foreach (UsingEntry ue in using_clauses){
					if (ue.Resolve () != null)
						continue;

					if (ue.resolved == null)
						Error_NamespaceNotFound (ue.Location, ue.Name.ToString ());
					else
						Report.Error (138, ue.Location,
							"`{0} is a type not a namespace. A using namespace directive can only be applied to namespaces", ue.Name);

				}
			}

			if (aliases != null){
				foreach (DictionaryEntry de in aliases){
					AliasEntry alias = (AliasEntry) de.Value;

					if (alias.Resolve () != null)
						continue;

					Error_NamespaceNotFound (alias.Location, alias.Alias.ToString ());
				}
			}
		}

		public override string ToString ()
		{
			if (NS == Namespace.Root)
				return "NamespaceEntry (<root>)";
			else
				return String.Format ("NamespaceEntry ({0},{1},{2})", ns.Name, IsImplicit, ID);
		}
	}
}
