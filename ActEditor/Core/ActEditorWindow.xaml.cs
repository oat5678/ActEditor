using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ActEditor.ApplicationConfiguration;
using ActEditor.Core.Scripting;
using ActEditor.Core.Scripting.Scripts.Effects;
using ActEditor.Core.WPF.Dialogs;
using ActEditor.Tools.GrfShellExplorer;
using ErrorManager;
using GRF.Core.GroupedGrf;
using GRF.FileFormats;
using GRF.FileFormats.ActFormat;
using GRF.FileFormats.SprFormat;
using GRF.GrfSystem;
using GRF.Threading;
using GrfToWpfBridge;
using GrfToWpfBridge.Application;
using GrfToWpfBridge.MultiGrf;
using TokeiLibrary;
using TokeiLibrary.Paths;
using TokeiLibrary.Shortcuts;
using TokeiLibrary.WPF;
using TokeiLibrary.WPF.Styles;
using Utilities;
using Utilities.CommandLine;
using Utilities.Extension;
using Action = System.Action;
using ActEditor.Core.Scripting.Scripts;
using GRF.Image;
using Newtonsoft.Json;

namespace ActEditor.Core
{
	/// <summary>
	/// Interaction logic for ActEditorWindow.xaml
	/// </summary>
	public partial class ActEditorWindow : TkWindow
	{
		#region Delegates

		public delegate void ActEditorEventDelegate(object sender);

		#endregion

		public static ActEditorWindow Instance { get; private set; }

		private readonly MultiGrfReader _metaGrf = new MultiGrfReader();
		private readonly MetaGrfResourcesViewer _metaGrfViewer = new MetaGrfResourcesViewer();
		private readonly WpfRecentFiles _recentFiles;
		private readonly ScriptLoader _scriptLoader;
		private readonly TabEngine _tabEngine;
		private EditorPosition _editorPosition = new EditorPosition();
		private SplashWindow _splashWindow;

		public RecentFilesManager RecentFiles
		{
			get { return _recentFiles; }
		}

		public ActEditorWindow()
			: base("Act Editor", "app.ico", SizeToContent.WidthAndHeight, ResizeMode.CanResize)
		{
			Instance = this;
			_initializeSplashWindow();

			InitializeComponent();

			_tabEngine = new TabEngine(_tabControl, this);
			_scriptLoader = new ScriptLoader(_mainMenu, _dpUndoRedo);
			_recentFiles = new WpfRecentFiles(ActEditorConfiguration.ConfigAsker, 6, _miOpenRecent, "Act");
			_recentFiles.FileClicked += new RecentFilesManager.RFMFileClickedEventHandler(_recentFiles_FileClicked);

			_initializeMenu();
			_initializeShortcuts();
			_miReverseAnchors.Checked += (e, s) => _tabEngine.ReverseAnchorChecked();
			_miReverseAnchors.Unchecked += (e, s) => _tabEngine.ReverseAnchorUnchecked();
			_miReverseAnchors.IsChecked = ActEditorConfiguration.ReverseAnchor;
			_initializeMetaGrf();
			_splashWindow.Display = "Loading custom scripts...";
		}

		private void _actEditorWindow_Loaded(object sender, RoutedEventArgs e)
		{
			// Allow modifying size on load
			SizeToContent = SizeToContent.Manual;
			_editorPosition.Load(this);
			_initializeScripts();

			try
			{
				if (!_parseCommandLineArguments())
				{
					if (_recentFiles.Files.Count > 0 && ActEditorConfiguration.ReopenLatestFile && _recentFiles.Files[0].IsExtension(".act") && File.Exists(new TkPath(_recentFiles.Files[0]).FilePath))
						_tabEngine.Open(_recentFiles.Files[0]);
				}
			}
			catch (Exception err)
			{
				ShowException(_splashWindow, err);
			}

			if (_tabControl.Items.Count == 0)
			{
				_miNew_Click(null, null);
			}

			_splashWindow.Terminate(400);
		}

		private void _tabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (_tabControl.SelectedIndex < 0)
				return;

			if (_tabControl.Items[_tabControl.SelectedIndex] is TabAct tabItem)
			{
				_tmbUndo.SetUndo(tabItem.Act.Commands);
				_tmbRedo.SetRedo(tabItem.Act.Commands);
			}
		}

		private void _initializeMetaGrf()
		{
			_metaGrfViewer.SaveResourceMethod = delegate (string resources) {
				ActEditorConfiguration.Resources = Methods.StringToList(resources);
				_metaGrfViewer.LoadResourcesInfo();
				_metaGrf.Update(_metaGrfViewer.Paths);
			};

			_metaGrfViewer.LoadResourceMethod = () => ActEditorConfiguration.Resources.Select(p => new MultiGrfPath(p) { FromConfiguration = true, IsCurrentlyLoadedGrf = false }).ToList();
			_metaGrfViewer.LoadResourcesInfo();
			GrfThread.Start(() => {
				try
				{
					_metaGrf.Update(_metaGrfViewer.Paths);
				}
				catch
				{
				}
			}, "ActEditor - MetaGrf loader");
		}

		private void _initializeShortcuts()
		{
			ApplicationShortcut.Link(ActEditorCommands.Undo, Undo, this);
			ApplicationShortcut.Link(ActEditorCommands.Redo, Redo, this);
			ApplicationShortcut.Link(ActEditorCommands.FrameEditorNextFrame, () => _tabEngine.FrameMove(1), this);
			ApplicationShortcut.Link(ActEditorCommands.FrameEditorPreviousFrame, () => _tabEngine.FrameMove(-1), this);
			ApplicationShortcut.Link(ActEditorCommands.FrameEditorNextAction, () => _tabEngine.ActionMove(1), this);
			ApplicationShortcut.Link(ActEditorCommands.FrameEditorPreviousAction, () => _tabEngine.ActionMove(-1), this);
			ApplicationShortcut.Link(ActEditorCommands.ActEditorStyleEditor, () => WindowProvider.Show(new StyleEditor(), new Control()), this);
			ApplicationShortcut.Link(ActEditorCommands.ActEditorStopPlayAnimation, () => _tabEngine.Execute(v => {
				if (v._frameSelector.IsPlaying)
					v._frameSelector.Stop();
				else
					v._frameSelector.Play();
			}), this);
			ApplicationShortcut.Link(ActEditorCommands.ActEditorNewFile, _miNew, this);
			ApplicationShortcut.Link(ActEditorCommands.ActEditorCloseTab, _miCloseCurrent, this);
			ApplicationShortcut.Link(ActEditorCommands.Open, _miOpen, this);
			ApplicationShortcut.Link(ActEditorCommands.Save, _miSave, this);
			ApplicationShortcut.Link(ActEditorCommands.SaveAs, _miSaveAs, this);
			ApplicationShortcut.Link(ActEditorCommands.ActEditorSaveAsGarment, _miSaveAsGarment, this);
			ApplicationShortcut.Link(ActEditorCommands.Copy, _miCopy, this);
			ApplicationShortcut.Link(ActEditorCommands.Paste, _miPaste, this);
			ApplicationShortcut.Link(ActEditorCommands.Cut, _miCut, this);
			ApplicationShortcut.Link(ActEditorCommands.FrameEditorShowAdjascentFrames, _miViewPrevAnim, this);
			ApplicationShortcut.Link(ActEditorCommands.FrameEditorShowAnchor1, _miAnchor1, this);
			ApplicationShortcut.Link(ActEditorCommands.FrameEditorShowAnchor2, _miAnchor2, this);
			ApplicationShortcut.Link(ActEditorCommands.FrameEditorShowAnchor3, _miAnchor3, this);
			ApplicationShortcut.Link(ActEditorCommands.FrameEditorShowAnchor4, _miAnchor4, this);
			ApplicationShortcut.Link(ActEditorCommands.FrameEditorShowAnchor5, _miAnchor5, this);
			ApplicationShortcut.Link(ActEditorCommands.Delete, () => _tabEngine.Execute(v => v.FrameRenderer.InteractionEngine.Delete()), this);
			ApplicationShortcut.Link(ActEditorCommands.LayerEditorBringOneUp, () => _tabEngine.Execute(v => v.LayerEditor.BringOneUp()), this);
			ApplicationShortcut.Link(ActEditorCommands.FrameEditorLayerMirrorVertical, () => _tabEngine.Execute(v => v.LayerEditor.MirrorFromOffset(FlipDirection.Vertical)), this);
			ApplicationShortcut.Link(ActEditorCommands.FrameEditorLayerMirrorHorizontal, () => _tabEngine.Execute(v => v.LayerEditor.MirrorFromOffset(FlipDirection.Horizontal)), this);
			ApplicationShortcut.Link(ActEditorCommands.ActEditorSelectActInExplorer, _miSelectAct, this);
			ApplicationShortcut.Link(ApplicationShortcut.FromString("F7", "Debug.ViewportGrid"), delegate {
				ActEditorConfiguration.ActEditorUnitGrid.Set(!ActEditorConfiguration.ActEditorUnitGrid.Get());
				GetCurrentTab2().FrameRenderer.Update();
			}, this);
			ApplicationShortcut.Link(ApplicationShortcut.FromString("R", "Debug.TestMethod"), delegate {
				//GC.Collect();
				GetCurrentTab2().FrameRenderer.Update();
				_tabEngine.Execute(v => {
					v.DummyScript();
				});
			}, this);

			try
			{
				ApplicationShortcut.OverrideBindings(ActEditorConfiguration.Remapper);
			}
			catch (Exception err)
			{
				try
				{
					ActEditorConfiguration.Remapper.Clear();
					ApplicationShortcut.OverrideBindings(ActEditorConfiguration.Remapper);
				}
				catch
				{
				}

				ErrorHandler.HandleException("Failed to load the custom key bindings. The bindings will be reset to their default values.", err);
			}
		}

		private void _initializeScripts()
		{
			ScriptLoaderResult result = null;

			try
			{
				ScriptLoader.VerifyExampleScriptsInstalled();
				result = _scriptLoader.ReloadScriptFolderToEditor(this, _mainMenu, _dpUndoRedo);
			}
			catch (Exception err)
			{
				ShowException(_splashWindow, err);
			}

			if (result != null && result.Errors.Count > 0)
			{
				Dispatcher.BeginInvoke(new Action(() =>
				{
					ScriptLoader.ShowErrors(result);
				}), DispatcherPriority.Render);
			}
		}

		private void _initializeSplashWindow()
		{
			_splashWindow = new SplashWindow();
			_splashWindow.Display = "Initializing components...";
			_splashWindow.Show();
		}

		public void ShowException(Window diag, Exception ex)
		{
			try
			{
				diag.Visibility = Visibility.Hidden;
				WindowProvider.WindowOpened += _windowProvider_WindowOpened;
				ErrorHandler.HandleException(ex);
			}
			finally
			{
				diag.Visibility = Visibility.Visible;
				WindowProvider.WindowOpened -= _windowProvider_WindowOpened;
			}
		}

		private void _windowProvider_WindowOpened(TkWindow window)
		{
			window.ShowInTaskbar = true;
		}

		private void Undo() => _tabEngine.Undo();
		private void Redo() => _tabEngine.Redo();
		public ScriptLoader ScriptLoader => _scriptLoader;
		public MultiGrfReader MetaGrf => _metaGrf;
		public TabEngine TabEngine => _tabEngine;

		private void _initializeMenu()
		{
			_splashWindow.Display = "Loading Act Editor's scripts...";
			_scriptLoader.AddScriptsToMenu(new SpriteExportNormal(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new SpriteExport(), this, _mainMenu);

			_scriptLoader.AddScriptsToMenu(new EditSelectAll(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new EditDeselectAll(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new InvertSelection(), this, _mainMenu);
			((MenuItem)_mainMenu.Items[1]).Items.Add(new Separator());
			_scriptLoader.AddScriptsToMenu(new BringToFront(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new BringToBack(), this, _mainMenu);
			((MenuItem)_mainMenu.Items[1]).Items.Add(new Separator());
			_scriptLoader.AddScriptsToMenu(new EditSound(), this, _mainMenu);
			((MenuItem)_mainMenu.Items[1]).Items.Add(new Separator());
			_scriptLoader.AddScriptsToMenu(new EditBackground(), this, _mainMenu);
			((MenuItem)_mainMenu.Items[1]).Items.Add(new Separator());
			_scriptLoader.AddScriptsToMenu(new EditClearPalette(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new EditPalette(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new EditPaletteAdvanced(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new ImportPaletteFrom(), this, _mainMenu);

			_scriptLoader.AddScriptsToMenu(new EditAnchor(), this, _mainMenu);
			((MenuItem)_mainMenu.Items[2]).Items.Add(new Separator());
			((MenuItem)_mainMenu.Items[2]).Items.Add(new TkMenuItem { Header = "Set anchors", IconPath = "forward.png" });
			_scriptLoader.AddScriptsToMenu(new ImportAnchor(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new ImportDefaultMaleAnchor(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new ImportDefaultFemaleAnchor(), this, _mainMenu);

			_scriptLoader.AddScriptsToMenu(new ActionCopy(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new ActionPaste(), this, _mainMenu);
			((MenuItem)_mainMenu.Items[3]).Items.Add(new Separator());
			_scriptLoader.AddScriptsToMenu(new ActionDelete(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new ActionAdd(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new ActionAdvanced(), this, _mainMenu);
			((MenuItem)_mainMenu.Items[3]).Items.Add(new Separator());
			_scriptLoader.AddScriptsToMenu(new ActionCopyMirror(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new ActionMirrorVertical(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new ActionMirrorHorizontal(), this, _mainMenu);
			((MenuItem)_mainMenu.Items[3]).Items.Add(new Separator());
			_scriptLoader.AddScriptsToMenu(new ActionLayerMove(ActionLayerMove.MoveDirection.Down, null), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new ActionLayerMove(ActionLayerMove.MoveDirection.Up, null), this, _mainMenu);

			_scriptLoader.AddScriptsToMenu(new FrameDelete(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new FrameAdd(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new FrameAdvanced(), this, _mainMenu);
			((MenuItem)_mainMenu.Items[4]).Items.Add(new Separator());
			_scriptLoader.AddScriptsToMenu(new FrameDuplicate(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new FrameAddLayerToAllFrames(), this, _mainMenu);
			((MenuItem)_mainMenu.Items[4]).Items.Add(new Separator());
			_scriptLoader.AddScriptsToMenu(new FrameMirrorVertical(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new FrameMirrorHorizontal(), this, _mainMenu);

			_scriptLoader.AddScriptsToMenu(new FrameCopyBrB(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new FrameCopyBBl(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new FrameCopyBBr(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new FrameCopyBlB(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new ReverseAnimation(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new FrameCopyHead(this), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new FrameCopyHead2(this), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new FrameCopyGarment(this), this, _mainMenu);
			((MenuItem)_mainMenu.Items[5]).Items.Add(new Separator());
			_scriptLoader.AddScriptsToMenu(new InterpolationAnimation(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new LayerInterpolationAnimation(), this, _mainMenu);
			((MenuItem)_mainMenu.Items[5]).Items.Add(new Separator());
			_scriptLoader.AddScriptsToMenu(new InterpolationAnimationAdv(), this, _mainMenu);

			// Idle
			_scriptLoader.AddScriptsToMenu(new FloatingEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new BleedingOutlineEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new FadeInOutColorOverlayEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new EffectBreathing(), this, _mainMenu);
			//_scriptLoader.AddScriptsToMenu(new CutSpriteEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new NoiseMaskEffect(), this, _mainMenu);
			// Attack
			_scriptLoader.AddScriptsToMenu(new TrailAttackEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new SimpleAttackEffect(), this, _mainMenu);
			// Hit
			_scriptLoader.AddScriptsToMenu(new HitEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new RecoilEffect(), this, _mainMenu);
			// Global
			_scriptLoader.AddScriptsToMenu(new EffectFadeAnimation(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new RadialErosionEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new SpikeErosionEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new VerticalStripeErosion(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new CrystalErosionEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new SmokeFadeEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new StrokeSilhouetteEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new SilhouetteDistortionEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new FloorAuraEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new DelayedShadowEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new SpriteOutlineEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new FadeParticleEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new SwirlEffect(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new BubbleEffect(), this, _mainMenu);

			_scriptLoader.AddScriptsToMenu(new ScriptRunnerMenu(), this, _mainMenu);
			((MenuItem)_mainMenu.Items[7]).Items.Add(new Separator());
			_scriptLoader.AddScriptsToMenu(new OpenScriptsFolder(), this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new ReloadScripts { ActEditor = this }, this, _mainMenu);
			_scriptLoader.AddScriptsToMenu(new BatchScriptMenu(), this, _mainMenu);
			((MenuItem)_mainMenu.Items[7]).Items.Add(new Separator());
			_scriptLoader.UpdateRedoUndoPosition(_mainMenu, _dpUndoRedo);

			Binder.Bind(_miViewSameAction, () => ActEditorConfiguration.KeepPreviewSelectionFromActionChange);
		}

		private bool _parseCommandLineArguments()
		{
			try
			{
				List<GenericCLOption> options = CommandLineParser.GetOptions(Environment.CommandLine, false);

				foreach (GenericCLOption option in options)
				{
					if (option.CommandName == "-REM" || option.CommandName == "REM")
					{
						return true;
					}
					else
					{
						if (option.Args.Count <= 0)
							continue;

						if (option.Args.All(p => p.IsExtension(".act")))
						{
							_tabEngine.Open(option.Args[0]);
							return true;
						}
					}
				}
			}
			catch (Exception err)
			{
				ErrorHandler.HandleException(err);
			}

			return false;
		}

		private void _actEditorWindow_Drop(object sender, DragEventArgs e)
		{
			try
			{
				if (e.Data.GetDataPresent(DataFormats.FileDrop, true))
				{
					string[] files = e.Data.GetData(DataFormats.FileDrop, true) as string[];
					_tabEngine.LastOpened = "";

					if (files != null && files.Length > 0 && files.Any(p => p.IsExtension(".act")))
					{
						_tabEngine.OpenFiles(files.Where(p => p.IsExtension(".act")).Select(p => new TkPath(p)).ToArray());
					}
				}
			}
			catch (Exception err)
			{
				ErrorHandler.HandleException(err);
			}
		}

		private void _actEditorWindow_DragEnter(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop, true))
			{
				string[] files = e.Data.GetData(DataFormats.FileDrop, true) as string[];

				if (files != null && files.Length > 0 && files.Any(p => p.IsExtension("*.act")))
				{
					e.Effects = DragDropEffects.All;
				}
			}
		}

		protected override void OnClosed(EventArgs e)
		{
			base.OnClosed(e);

			_metaGrf?.Dispose();
			_scriptLoader?.Dispose();
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			try
			{
				var tabs = _tabControl.Items.OfType<TabAct>().ToList();

				foreach (var tab in tabs)
				{
					if (tab.Act.Commands.IsModified)
					{
						tab.IsSelected = true;

						if (!_tabEngine.CloseAct(tab))
						{
							e.Cancel = true;
							return;
						}
					}
				}

				if (tabs.Any(tab => !_tabEngine.CloseAct(tab)))
				{
					e.Cancel = true;
					return;
				}
			}
			catch (Exception err)
			{
				ErrorHandler.HandleException(err);
			}

			_editorPosition.Save(this);
			base.OnClosing(e);
			ApplicationManager.Shutdown();
		}

		private void _recentFiles_FileClicked(string file) => _tabEngine.Open(file);

		protected override void GRFEditorWindowKeyDown(object sender, KeyEventArgs e)
		{
		}

		private void _miOpen_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				string file = TkPathRequest.OpenFile<ActEditorConfiguration>("ExtractingServiceLastPath", "filter", FileFormat.MergeFilters(FileFormat.Act));

				if (file != null)
				{
					_tabEngine.Open(file);
				}
			}
			catch (Exception err)
			{
				ErrorHandler.HandleException(err);
			}
			finally
			{
				_tabEngine.RestoreFocus();
			}
		}

		private void _miSettings_Click(object sender, RoutedEventArgs e)
		{
			WindowProvider.Show(new ActEditorSettings(_metaGrfViewer), _miSettings, this);
		}

		private void _miClose_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var tabs = _tabControl.Items.OfType<TabAct>().ToList();

				foreach (var tab in tabs)
				{
					if (tab.Act.Commands.IsModified)
					{
						tab.IsSelected = true;

						if (!_tabEngine.CloseAct(tab))
						{
							return;
						}
					}
				}

				if (tabs.Any(tab => !_tabEngine.CloseAct(tab)))
				{
					return;
				}

				Close();
			}
			catch (Exception err)
			{
				ErrorHandler.HandleException(err);
			}
		}

		private void _miNew_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var act = new Act(new Spr());
				act.AddAction();
				_specialLoad(act, TemporaryFilesManager.GetTemporaryFilePath("new_{0:0000}"));
			}
			catch (Exception err)
			{
				ErrorHandler.HandleException(err);
			}
			finally
			{
				_tabEngine.RestoreFocus();
			}
		}

		public bool Save() => _tabEngine.Save();
		public bool SaveAs() => _tabEngine.SaveAs();
		private void _miSave_Click(object sender, RoutedEventArgs e) => Save();
		private void _miSaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();

		private void _miSaveAsGarment_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var dialog = new SaveGarmentDialog(this);
				dialog.Owner = WpfUtilities.TopWindow;

				dialog.Closing += delegate {
					dialog.Owner.Focus();
					_miSaveAsGarment.IsEnabled = true;
				};

				_miSaveAsGarment.IsEnabled = false;
				dialog.Show();
			}
			catch (Exception err)
			{
				ErrorHandler.HandleException(err);
			}
		}

		private void _miCloseCurrent_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				_tabEngine.CloseAct();
			}
			catch (Exception err)
			{
				ErrorHandler.HandleException(err);
			}
			finally
			{
				_tabEngine.RestoreFocus();
			}
		}

		private void _miAbout_Click(object sender, RoutedEventArgs e)
		{
			var dialog = new AboutDialog(ActEditorConfiguration.PublicVersion, ActEditorConfiguration.RealVersion, ActEditorConfiguration.Author, ActEditorConfiguration.ProgramName);
			dialog.Owner = WpfUtilities.TopWindow;
			((TextBox)dialog.FindName("_textBlock")).Text += "\r\n\r\nCredits: Nebraskka (suggestions and feedback)";
			dialog.AboutTextBox.Background = this.FindResource("UIThemeAboutDialogBrush") as Brush;
			dialog.ShowDialog();
			_tabEngine.RestoreFocus();
		}

		private void _miOpenFromGrf_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				string file = TkPathRequest.OpenFile<ActEditorConfiguration>("AppLastGrfPath", "filter", FileFormat.MergeFilters(FileFormat.AllContainers, FileFormat.Grf, FileFormat.Gpf, FileFormat.Thor));

				if (file != null)
				{
					GrfExplorer dialog = new GrfExplorer(file, SelectMode.Act);
					dialog.Owner = WpfUtilities.TopWindow;

					if (dialog.ShowDialog() == true)
					{
						string relativePath = dialog.SelectedItem;

						if (relativePath == null) return;

						if (!relativePath.IsExtension(".act"))
						{
							throw new Exception("Only ACT files can be selected.");
						}

						_tabEngine.Open(new TkPath(file, relativePath));
					}
				}
			}
			catch (Exception err)
			{
				ErrorHandler.HandleException(err);
			}
			finally
			{
				_tabEngine.RestoreFocus();
			}
		}

		private void _miCopy_Click(object sender, RoutedEventArgs e) => _tabEngine.Copy();
		private void _miPaste_Click(object sender, RoutedEventArgs e) => _tabEngine.Paste();
		private void _miCut_Click(object sender, RoutedEventArgs e) => _tabEngine.Cut();

		private void _miAnchor_Click(object sender, RoutedEventArgs e)
		{
			foreach (MenuItem item in _miAnchor.Items)
			{
				if (item != sender)
					item.IsChecked = false;
				else
				{
					item.Checked -= _miAnchor_Click;
					item.IsChecked = true;
					item.Checked += _miAnchor_Click;
				}
			}

			_tabEngine?.SetAnchorIndex(Int32.Parse(((FrameworkElement)sender).Tag.ToString()));
		}

		private void _miSelectAct_Click(object sender, RoutedEventArgs e) => _tabEngine.Select();

		private void _miShowAnchors_Loaded(object sender, RoutedEventArgs e)
		{
			_miShowAnchors.IsChecked = ActEditorConfiguration.ShowAnchors;
		}

		private void _miShowAnchors_Checked(object sender, RoutedEventArgs e)
		{
			ActEditorConfiguration.ShowAnchors = true;
			_tabEngine.RendererUpdate();
		}

		private void _miShowAnchors_Unchecked(object sender, RoutedEventArgs e)
		{
			ActEditorConfiguration.ShowAnchors = false;
			_tabEngine.RendererUpdate();
		}

		private void _miNewHeadgear_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var act = new Act(ApplicationManager.GetResource("ref_head_f.act"), new Spr());
				act.AllFrames(p => p.Layers.Clear());
				_specialLoad(act, TemporaryFilesManager.GetTemporaryFilePath("new_{0:0000}"));
			}
			catch (Exception err)
			{
				ErrorHandler.HandleException(err);
			}
			finally
			{
				_tabEngine.RestoreFocus();
			}
		}

		private void _specialLoad(Act act, string fileName)
		{
			act.LoadedPath = fileName + ".act";
			act.Sprite.Save(fileName + ".spr");
			act.Save(fileName + ".act");

			_tabEngine.Open(fileName + ".act", isNew: true);
			_recentFiles.RemoveRecentFile(fileName + ".act");
		}

		private void _new(string name)
		{
			try
			{
				var act = new Act(ApplicationManager.GetResource(name), new Spr());
				_specialLoad(act, TemporaryFilesManager.GetTemporaryFilePath("new_{0:0000}"));
			}
			catch (Exception err)
			{
				ErrorHandler.HandleException(err);
			}
			finally
			{
				_tabEngine.RestoreFocus();
			}
		}

		private void _miNewNpc_Click(object sender, RoutedEventArgs e) => _new("NPC.act");
		private void _miNewWeapon_Click(object sender, RoutedEventArgs e) => _new("weapon.act");
		private void _miNewMonster_Click(object sender, RoutedEventArgs e) => _new("monster.act");
		private void _miNewHomunculus_Click(object sender, RoutedEventArgs e) => _new("homunculus.act");

		private void _miNewHeadgearMale_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var act = new Act(ApplicationManager.GetResource("ref_head_m.act"), new Spr());
				act.AllFrames(p => p.Layers.Clear());
				_specialLoad(act, TemporaryFilesManager.GetTemporaryFilePath("new_{0:0000}"));
			}
			catch (Exception err)
			{
				ErrorHandler.HandleException(err);
			}
			finally
			{
				_tabEngine.RestoreFocus();
			}
		}

		private void _miViewPrevAnim_Click(object sender, RoutedEventArgs e) => _tabEngine.ShowPreviewFrames();

		public TabAct GetCurrentTab2()
		{
			var tab = _tabEngine.GetCurrentTab();

			if (tab == null)
				throw new Exception("No file opened. Please open an ACT file before using this command.");

			return tab;
		}

		// ==============================================================
		// โค้ดส่วนดึง JSON ของ PixelLab (รองรับ 8 และ 4 ทิศทาง + เซฟ .spr สมบูรณ์)
		// ==============================================================
		private void _miImportPixelLab_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var tab = _tabEngine.GetCurrentTab();
				if (tab == null)
				{
					MessageBox.Show("Please open an Act file first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
					return;
				}

				// แยกค่าโหมดทิศทาง (8 หรือ 4) และ Base Action Index (0, 8, 16, 24, 32) จาก Tag เช่น "8_0" หรือ "4_8"
				int directionMode = 8;
				int baseAction = 0;

				if (sender is MenuItem menuItem && menuItem.Tag != null)
				{
					string tagStr = menuItem.Tag.ToString();
					if (tagStr.Contains("_"))
					{
						string[] parts = tagStr.Split('_');
						int.TryParse(parts[0], out directionMode);
						int.TryParse(parts[1], out baseAction);
					}
					else
					{
						int.TryParse(tagStr, out baseAction);
					}
				}

				Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
				dlg.Filter = "JSON Files (*.json)|*.json";

				if (dlg.ShowDialog() == true)
				{
					ImportPixelLabJson(dlg.FileName, tab.Act, baseAction, directionMode);

					// รีเฟรชทั้งหน้าจอกลางและแถบ Sprite ด้านล่างทันที
					tab.Act.InvalidateVisual();
					tab.Act.InvalidateSpriteVisual();
					tab.FrameRenderer.Update();
					tab._spriteSelector.Update();

					MessageBox.Show($"Import ({directionMode}-Dir Mode) finished into Action {baseAction} - {baseAction + 7}!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
				}
			}
			catch (Exception ex)
			{
				ErrorHandler.HandleException(ex);
			}
		}

		private int[] GetTargetActions(string direction, int row, int baseAction, int directionMode)
		{
			if (directionMode == 4)
			{
				if (!string.IsNullOrEmpty(direction))
				{
					switch (direction.ToLowerInvariant())
					{
						case "south-west": // Row 4 -> จับคู่ Action Index 0 กับ 1
							return new int[] { baseAction + 0, baseAction + 1 };
						case "north-west": // Row 3 -> จับคู่ Action Index 2 กับ 3
							return new int[] { baseAction + 2, baseAction + 3 };
						case "north-east": // Row 2 -> จับคู่ Action Index 4 กับ 5
							return new int[] { baseAction + 4, baseAction + 5 };
						case "south-east": // Row 1 -> จับคู่ Action Index 6 กับ 7
							return new int[] { baseAction + 6, baseAction + 7 };
					}
				}

				// กรณีสำรองสำหรับ 4 ทิศทาง: อิงตามหมายเลข Row (1 ถึง 4)
				switch (row)
				{
					case 4: return new int[] { baseAction + 0, baseAction + 1 };
					case 3: return new int[] { baseAction + 2, baseAction + 3 };
					case 2: return new int[] { baseAction + 4, baseAction + 5 };
					case 1: return new int[] { baseAction + 6, baseAction + 7 };
					default: return new int[0];
				}
			}
			else
			{
				int offset = -1;
				if (!string.IsNullOrEmpty(direction))
				{
					switch (direction.ToLowerInvariant())
					{
						case "south": offset = 0; break;
						case "south-west": offset = 1; break;
						case "west": offset = 2; break;
						case "north-west": offset = 3; break;
						case "north": offset = 4; break;
						case "north-east": offset = 5; break;
						case "east": offset = 6; break;
						case "south-east": offset = 7; break;
					}
				}

				// กรณีสำรองสำหรับ 8 ทิศทาง: อิงตามลำดับแถวของ PixelLab (Row 1 ถึง 8)
				if (offset < 0 && row >= 1 && row <= 8)
				{
					int[] fallbackOffsets = new int[] { -1, 0, 7, 6, 5, 4, 3, 2, 1 };
					offset = fallbackOffsets[row];
				}

				return offset >= 0 ? new int[] { baseAction + offset } : new int[0];
			}
		}

		private void ImportPixelLabJson(string jsonPath, Act currentAct, int baseAction, int directionMode)
		{
			string jsonContent = File.ReadAllText(jsonPath);
			var data = JsonConvert.DeserializeObject<PixelLabExport>(jsonContent);

			string dir = Path.GetDirectoryName(jsonPath);
			string imgPath = Path.Combine(dir, data.spritesheet.path);

			if (!File.Exists(imgPath))
			{
				MessageBox.Show("Image file not found: " + imgPath, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			System.Drawing.Bitmap sourceBmp = new System.Drawing.Bitmap(imgPath);

			int cellWidth = data.spritesheet.cell_size.width;
			int cellHeight = data.spritesheet.cell_size.height;

			string tempPngPath = Path.Combine(Path.GetTempPath(), "pixellab_temp_frame.png");

			currentAct.Commands.Backup(_ =>
			{
				while (currentAct.Actions.Count <= baseAction + 7)
				{
					currentAct.Actions.Add(new GRF.FileFormats.ActFormat.Action());
				}

				if (data.spritesheet.rows != null && data.spritesheet.rows.Count > 0)
				{
					foreach (var rowItem in data.spritesheet.rows)
					{
						if (rowItem.type == "rotations")
							continue;

						int[] targetActions = GetTargetActions(rowItem.direction, rowItem.row, baseAction, directionMode);
						if (targetActions.Length == 0)
							continue;

						int frameCount = rowItem.frame_count > 0 ? rowItem.frame_count : data.spritesheet.columns;

						foreach (int targetAction in targetActions)
						{
							currentAct.Actions[targetAction].Frames.Clear();
						}

						for (int col = 0; col < frameCount; col++)
						{
							System.Drawing.Rectangle cropRect = new System.Drawing.Rectangle(col * cellWidth, rowItem.row * cellHeight, cellWidth, cellHeight);
							using (System.Drawing.Bitmap frameBmp = sourceBmp.Clone(cropRect, sourceBmp.PixelFormat))
							{
								frameBmp.Save(tempPngPath, System.Drawing.Imaging.ImageFormat.Png);
							}

							var grfImage = new GrfImage(tempPngPath);
							grfImage.Convert(GRF.Image.GrfImageType.Bgra32);

							// เพิ่มรูปเข้า .spr เพียงครั้งเดียวต่อเฟรม เพื่อไม่ให้รูปซ้ำซ้อนในไฟล์ .spr
							currentAct.Sprite.InsertAny(grfImage);
							int newSpriteIndex = currentAct.Sprite.Images.Count - 1;

							// นำ SpriteIndex เดียวกันไปใส่ให้ครบทุก Action Index ที่จับคู่ไว้
							foreach (int targetAction in targetActions)
							{
								var layer = new GRF.FileFormats.ActFormat.Layer(newSpriteIndex, currentAct.Sprite);
								var newFrame = new GRF.FileFormats.ActFormat.Frame();
								newFrame.Layers.Add(layer);

								currentAct.Actions[targetAction].Frames.Add(newFrame);
							}
						}
					}
				}
			}, $"Import PixelLab JSON ({directionMode}-Dir)");

			sourceBmp.Dispose();

			if (File.Exists(tempPngPath))
			{
				File.Delete(tempPngPath);
			}
		}

		private void _miConvertToIndexed8_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var tab = _tabEngine.GetCurrentTab();
				if (tab == null)
				{
					MessageBox.Show("Please open an Act file first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
					return;
				}

				var act = tab.Act;
				if (act.Sprite.NumberOfBgra32Images == 0)
				{
					MessageBox.Show("No Bgra32 images found to convert.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
					return;
				}

				// เรียกใช้ SpriteManager ของระบบโดยตรงเพื่อจัดการ Palette และอัปเดต Layer Index ให้ครบถ้วน
				SpriteManager spriteManager = new SpriteManager();
				spriteManager.Init(tab);

				// ตั้งค่าเป็น -1 เพื่อให้หน้าต่าง Format conflict เด้งถามแค่ครั้งเดียว
				// (หากต้องการข้ามหน้าต่าง Format conflict ให้ใช้โหมด Merge (original) อัตโนมัติ สามารถเปลี่ยนเลข -1 เป็น 2 ได้เลย)
				SpriteManager.SpriteConverterOption = -1;

				int convertedCount = 0;

				try
				{
					act.Commands.ActEditBegin("Convert all sprites to Indexed8");

					while (act.Sprite.NumberOfBgra32Images > 0)
					{
						// ภาพ Bgra32 ตัวแรกจะอยู่ต่อท้ายภาพ Indexed8 เสมอ
						int firstBgra32AbsIndex = act.Sprite.NumberOfIndexed8Images;
						SpriteIndex index = SpriteIndex.FromAbsoluteIndex(firstBgra32AbsIndex, act.Sprite);

						spriteManager.ConvertImage(index);
						convertedCount++;
					}
				}
				catch (OperationCanceledException)
				{
					// กรณีผู้ใช้กดปุ่ม Cancel ในหน้าต่าง Format conflict จะยกเลิกการแปลงทั้งหมด
					act.Commands.ActCancelEdit();
					return;
				}
				catch
				{
					act.Commands.ActCancelEdit();
					throw;
				}
				finally
				{
					act.Commands.ActEditEnd();
				}

				// อัปเดตหน้าจอ UI ทั้งหมด
				tab.Act.InvalidateVisual();
				tab.Act.InvalidateSpriteVisual();
				tab.FrameRenderer.Update();
				tab._spriteSelector.Update();

				MessageBox.Show($"Successfully converted {convertedCount} images to Indexed8!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
			}
			catch (Exception ex)
			{
				ErrorHandler.HandleException(ex);
			}
		}
	}
}