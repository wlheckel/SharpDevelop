﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.Editor;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Parser;

namespace ICSharpCode.SharpDevelop.Refactoring
{
	/// <summary>
	/// Contains information about code around the caret in the editor - useful for implementing Context actions.
	/// Do not keep your own references to EditorContext.
	/// It serves as one-time cache and does not get updated when editor text changes.
	/// </summary>
	public class EditorContext
	{
		readonly object syncRoot = new object();
		readonly ITextEditor editor;
		
		/// <summary>
		/// The text editor for which this editor context was created.
		/// </summary>
		public ITextEditor Editor {
			get {
				WorkbenchSingleton.AssertMainThread();
				return editor;
			}
		}
		
		/// <summary>
		/// Gets/Sets the file name.
		/// </summary>
		public FileName FileName { get; private set; }
		
		/// <summary>
		/// A snapshot of the editor content, at the time when this editor context was created.
		/// </summary>
		public ITextSource TextSource { get; private set; }
		
		readonly int caretOffset;
		readonly TextLocation caretLocation;
		
		/// <summary>
		/// Gets the offset of the caret, at the time when this editor context was created.
		/// </summary>
		public int CaretOffset {
			get { return caretOffset; }
		}
		
		/// <summary>
		/// Gets caret location, at the time when this editor context was created.
		/// </summary>
		public TextLocation CaretLocation {
			get { return caretLocation; }
		}
		
		Task<ParseInformation> parseInformation;
		Task<ICompilation> compilation;
		
		/// <summary>
		/// Gets the ParseInformation for the file.
		/// </summary>
		/// <remarks><inheritdoc cref="ParserService.ParseAsync"/></remarks>
		public Task<ParseInformation> GetParseInformationAsync()
		{
			lock (syncRoot) {
				if (parseInformation == null)
					parseInformation = ParserService.ParseAsync(this.FileName, this.TextSource);
				return parseInformation;
			}
		}
		
		/// <summary>
		/// Gets the ICompilation for the file.
		/// </summary>
		public Task<ICompilation> GetCompilationAsync()
		{
			lock (syncRoot) {
				if (compilation == null)
					compilation = Task.FromResult(ParserService.GetCompilationForFile(this.FileName));
				return compilation;
			}
		}
		
		/// <summary>
		/// Caches values shared by Context actions. Used in <see cref="GetCached"/>.
		/// </summary>
		readonly ConcurrentDictionary<Type, Task> cachedValues = new ConcurrentDictionary<Type, Task>();
		
		/// <summary>
		/// Fully initializes the EditorContext.
		/// </summary>
		public EditorContext(ITextEditor editor)
		{
			if (editor == null)
				throw new ArgumentNullException("editor");
			this.editor = editor;
			caretOffset = editor.Caret.Offset;
			caretLocation = editor.Caret.Location;
			
			this.FileName = editor.FileName;
			this.TextSource = editor.Document.CreateSnapshot();
		}
		
		Task<ResolveResult> currentSymbol;
		
		/// <summary>
		/// The resolved symbol at editor caret.
		/// </summary>
		public Task<ResolveResult> GetCurrentSymbolAsync()
		{
			lock (syncRoot) {
				if (currentSymbol == null)
					currentSymbol = ResolveCurrentSymbolAsync();
				return currentSymbol;
			}
		}
		
		async Task<ResolveResult> ResolveCurrentSymbolAsync()
		{
			var parser = ParserService.GetParser(this.FileName);
			if (parser == null)
				return null;
			var parseInfo = await GetParseInformationAsync().ConfigureAwait(false);
			if (parseInfo == null)
				return null;
			var compilation = await GetCompilationAsync().ConfigureAwait(false);
			return await Task.Run(() => ParserService.ResolveAsync(this.FileName, caretLocation, this.TextSource, CancellationToken.None)).ConfigureAwait(false);
		}
		
		/// <summary>
		/// Gets cached value shared by context actions. Initializes a new value if not present.
		/// </summary>
		public Task<T> GetCachedAsync<T>(Func<EditorContext, T> initializationFunc)
		{
			return (Task<T>)cachedValues.GetOrAdd(typeof(T), _ => Task.FromResult(initializationFunc(this)));
		}
		
		/// <summary>
		/// Gets cached value shared by context actions. Initializes a new value if not present.
		/// </summary>
		public Task<T> GetCachedAsync<T>(Func<EditorContext, Task<T>> initializationFunc)
		{
			return (Task<T>)cachedValues.GetOrAdd(typeof(T), _ => initializationFunc(this));
		}
	}
}
