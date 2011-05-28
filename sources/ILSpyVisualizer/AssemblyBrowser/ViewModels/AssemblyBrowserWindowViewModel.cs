﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ILSpyVisualizer.Infrastructure;
using Mono.Cecil;
using System.Windows.Threading;
using QuickGraph;
using System.Windows.Input;
using ILSpyVisualizer.AssemblyBrowser.Screens;

namespace ILSpyVisualizer.AssemblyBrowser.ViewModels
{
	class AssemblyBrowserWindowViewModel : ViewModelBase
	{
		#region // Nested types

		class NavigationItem
		{
			public NavigationItem(Screen screen)
			{
				Screen = screen;
			}

			public NavigationItem(TypeViewModel type)
			{
				Type = type;
			}

			public Screen Screen { get; private set; }
			public TypeViewModel Type { get; private set; }

			public bool IsScreen
			{
				get { return Screen != null; }
			}
		}

		#endregion

		#region // Private fields

		private readonly Dispatcher _dispatcher;
		private readonly ObservableCollection<AssemblyViewModel> _assemblies;
		private IEnumerable<TypeDefinition> _allTypeDefinitions;
		private IEnumerable<TypeViewModel> _types;
		private Screen _screen;
		private readonly SearchScreen _searchScreen;

		private readonly Stack<NavigationItem> _previousNavigationItems = new Stack<NavigationItem>();
		private readonly Stack<NavigationItem> _nextNavigationItems = new Stack<NavigationItem>();

		#endregion

		#region // .ctor

		public AssemblyBrowserWindowViewModel(IEnumerable<AssemblyDefinition> assemblyDefinitions,
											  Dispatcher dispatcher)
		{
			_dispatcher = dispatcher;

			_assemblies = new ObservableCollection<AssemblyViewModel>(
								assemblyDefinitions.Select(a => new AssemblyViewModel(a, this)));

			_searchScreen = new SearchScreen(this);
			Screen = _searchScreen;

			OnAssembliesChanged();

			NavigateBackCommand = new DelegateCommand(NavigateBackCommandHandler);
			NavigateForwardCommand = new DelegateCommand(NavigateForwardCommandHandler);
		}

		#endregion

		#region // Public properties

		public ICommand NavigateBackCommand { get; private set; }
		public ICommand NavigateForwardCommand { get; private set; }

		public Screen Screen
		{
			get { return _screen; }
			set
			{
				_screen = value;
				AreAssembliesEditable = value.AllowAssemblyDrop;
				OnPropertyChanged("Screen");
			}
		}

		public IEnumerable<TypeDefinition> AllTypeDefinitions
		{
			get
			{
				if (_allTypeDefinitions == null)
				{
					_allTypeDefinitions = _assemblies
						.Select(a => a.AssemblyDefinition)
						.SelectMany(a => a.Modules)
						.SelectMany(m => m.Types)
						.ToList();
				}
				return _allTypeDefinitions;
			}
		}

		public string Title
		{
			get { return "Assembly Browser"; }
		}

		public bool ShowNavigationArrows
		{
			get { return true; }
		}

		public bool CanNavigateBack
		{
			get { return _previousNavigationItems.Count > 0; }
		}

		public bool CanNavigateForward
		{
			get { return _nextNavigationItems.Count > 0; }
		}

		public IEnumerable<TypeViewModel> Types
		{
			get { return _types; }
		}

		public Dispatcher Dispatcher
		{
			get { return _dispatcher; }
		}

		public ObservableCollection<AssemblyViewModel> Assemblies
		{
			get { return _assemblies; }
		}

		public UserCommand ShowSearchUserCommand
		{
			get { return new UserCommand("Search", new DelegateCommand(ShowSearch)); }
		}

		#endregion

		#region // Private properties

		private bool AreAssembliesEditable
		{
			set
			{
				foreach (var assembly in Assemblies)
				{
					assembly.ShowRemoveCommand = value;
				}
			}
		}

		private NavigationItem CurrentNavigationItem
		{
			get
			{
				var graphScreen = Screen as GraphScreen;
				if (graphScreen != null)
				{
					return new NavigationItem(graphScreen.Type);
				}
				return new NavigationItem(Screen);
			}
			set
			{
				if (value.IsScreen)
				{
					Screen = value.Screen;
				}
				else
				{
					var graphScreen = Screen as GraphScreen;
					if (graphScreen == null)
					{
						graphScreen = new GraphScreen(this);
						Screen = graphScreen;
					}
					graphScreen.Show(value.Type);
				}
			}
		}

		#endregion

		#region // Public methods

		public void ShowSearch()
		{
			if (Screen == _searchScreen)
			{
				_searchScreen.FocusSearchField();
				return;
			}

			Navigate(new NavigationItem(_searchScreen));
		}

		public void ShowGraph(TypeViewModel type)
		{
			Navigate(new NavigationItem(type));
		}

		public void AddAssemblies(IEnumerable<AssemblyDefinition> assemblies)
		{
			var newAssemblies = assemblies.Except(_assemblies.Select(a => a.AssemblyDefinition));
			if (newAssemblies.Count() == 0)
			{
				return;
			}

			foreach (var assembly in newAssemblies)
			{
				_assemblies.Add(new AssemblyViewModel(assembly, this));
			}

			OnAssembliesChanged();
		}

		public void AddAssembly(AssemblyDefinition assemblyDefinition)
		{
			if (_assemblies.Any(vm => vm.AssemblyDefinition == assemblyDefinition))
			{
				return;
			}

			_assemblies.Add(new AssemblyViewModel(assemblyDefinition, this));

			OnAssembliesChanged();
		}

		public void RemoveAssembly(AssemblyDefinition assemblyDefinition)
		{
			var assemblyViewModel = _assemblies
				.FirstOrDefault(vm => vm.AssemblyDefinition == assemblyDefinition);
			if (assemblyViewModel != null)
			{
				_assemblies.Remove(assemblyViewModel);
				OnAssembliesChanged();
			}
		}

		#endregion

		#region // Private methods

		private void OnAssembliesChanged()
		{
			UpdateInternalTypeCollections();
			_searchScreen.NotifyAssembliesChanged();
		}

		private void UpdateInternalTypeCollections()
		{
			_allTypeDefinitions = null;
			_types = AllTypeDefinitions
				.Select(t => new TypeViewModel(t, this))
				.ToList();
			var typesDictionary = _types.ToDictionary(type => type.TypeDefinition);

			foreach (var typeDefinition in AllTypeDefinitions
				.Where(t => t.BaseType != null))
			{
				var baseType = typeDefinition.BaseType.Resolve();
				if (typesDictionary.ContainsKey(baseType))
				{
					typesDictionary[baseType].AddDerivedType(
						typesDictionary[typeDefinition]);
				}
			}

			foreach (var type in Types)
			{
				type.CountDescendants();
			}
		}

		private void Navigate(NavigationItem item)
		{
			if (Screen != null)
			{
				_previousNavigationItems.Push(CurrentNavigationItem);
			}

			CurrentNavigationItem = item;
			_nextNavigationItems.Clear();

			RefreshNavigationCommands();
		}

		private void RefreshNavigationCommands()
		{
			OnPropertyChanged("CanNavigateBack");
			OnPropertyChanged("CanNavigateForward");
		}

		#endregion

		#region // Command handlers

		private void NavigateBackCommandHandler()
		{
			if (_previousNavigationItems.Count == 0)
			{
				return;
			}
			_nextNavigationItems.Push(CurrentNavigationItem);
			CurrentNavigationItem = _previousNavigationItems.Pop();

			RefreshNavigationCommands();
		}

		private void NavigateForwardCommandHandler()
		{
			if (_nextNavigationItems.Count == 0)
			{
				return;
			}
			
			_previousNavigationItems.Push(CurrentNavigationItem);
			CurrentNavigationItem = _nextNavigationItems.Pop();

			RefreshNavigationCommands();
		}

		#endregion
	}
}