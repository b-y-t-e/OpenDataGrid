using Open.DataGrid.Controls;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Controls
{
    /// <summary>
    /// Unit tests for GridLifecycleController — pure C# state machine, no WPF required.
    /// Covers: initial state, every named transition, all guards, and the critical
    /// Unloaded-isolation invariant (DataSource binding while detached must not clear IsUnloaded).
    /// </summary>
    [TestFixture]
    public class GridLifecycleControllerTests
    {
        private GridLifecycleController _ctrl;

        [SetUp]
        public void SetUp() => _ctrl = new GridLifecycleController();

        // --------------- Initial state ---------------

        [Test]
        public void InitialState_IsNoTemplate()
        {
            // Given / When — freshly constructed controller

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.NoTemplate));
            Assert.That(_ctrl.CanHandleInput, Is.False);
            Assert.That(_ctrl.IsInitialized,  Is.False);
            Assert.That(_ctrl.IsUnloaded,     Is.False);
            Assert.That(_ctrl.NeedsViewport,  Is.False);
        }

        // --------------- OnTemplateApplied ---------------

        [Test]
        public void OnTemplateApplied_FromNoTemplate_TransitionsToTemplateReady()
        {
            // Given — NoTemplate (initial)

            // When
            _ctrl.OnTemplateApplied();

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.TemplateReady));
        }

        [Test]
        public void OnTemplateApplied_FromReady_BacksOffToTemplateReady()
        {
            // Given
            SetState(GridLifecycleState.Ready);

            // When
            _ctrl.OnTemplateApplied();

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.TemplateReady));
        }

        [Test]
        public void OnTemplateApplied_WhenUnloaded_IsNoOp()
        {
            // Given — WPF can call OnApplyTemplate during re-mount before the Loaded event
            _ctrl.OnGridUnloaded();

            // When
            _ctrl.OnTemplateApplied();

            // Then — must remain Unloaded so OnGridLoaded still re-attaches CollectionChanged
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.Unloaded));
            Assert.That(_ctrl.IsUnloaded, Is.True);
        }

        // --------------- BeginReinitialize ---------------

        [Test]
        public void BeginReinitialize_FromReady_TransitionsToTemplateReady()
        {
            // Given
            SetState(GridLifecycleState.Ready);

            // When
            _ctrl.BeginReinitialize();

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.TemplateReady));
            Assert.That(_ctrl.CanHandleInput, Is.False);
        }

        [Test]
        public void BeginReinitialize_WhenUnloaded_IsNoOp()
        {
            // Given
            _ctrl.OnGridUnloaded();

            // When
            _ctrl.BeginReinitialize();

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.Unloaded));
        }

        // --------------- OnAsyncInitStarted ---------------

        [Test]
        public void OnAsyncInitStarted_FromTemplateReady_TransitionsToInitializing()
        {
            // Given
            _ctrl.OnTemplateApplied();

            // When
            _ctrl.OnAsyncInitStarted();

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.Initializing));
        }

        // --------------- OnInitializationComplete ---------------

        [Test]
        public void OnInitializationComplete_CanvasAndData_TransitionsToReady()
        {
            // Given
            _ctrl.OnTemplateApplied();

            // When
            _ctrl.OnInitializationComplete(canvasReady: true, hasData: true);

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.Ready));
            Assert.That(_ctrl.CanHandleInput, Is.True);
            Assert.That(_ctrl.IsInitialized,  Is.True);
        }

        [Test]
        public void OnInitializationComplete_NoCanvas_TransitionsToWaitingForViewport()
        {
            // Given
            _ctrl.OnTemplateApplied();

            // When
            _ctrl.OnInitializationComplete(canvasReady: false, hasData: true);

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.WaitingForViewport));
            Assert.That(_ctrl.NeedsViewport,  Is.True);
            Assert.That(_ctrl.CanHandleInput, Is.False);
        }

        [Test]
        public void OnInitializationComplete_CanvasButNoData_StaysTemplateReady()
        {
            // Given
            _ctrl.OnTemplateApplied();

            // When
            _ctrl.OnInitializationComplete(canvasReady: true, hasData: false);

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.TemplateReady));
        }

        [Test]
        public void OnInitializationComplete_NoCanvasNoData_StaysTemplateReady()
        {
            // Given
            _ctrl.OnTemplateApplied();

            // When
            _ctrl.OnInitializationComplete(canvasReady: false, hasData: false);

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.TemplateReady));
        }

        [Test]
        public void OnInitializationComplete_WhenUnloaded_IsNoOp()
        {
            // Given — DataSource binding fires while grid is detached from visual tree
            _ctrl.OnGridUnloaded();

            // When
            _ctrl.OnInitializationComplete(canvasReady: true, hasData: true);

            // Then — must remain Unloaded; OnGridLoaded depends on this to re-attach CollectionChanged
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.Unloaded));
            Assert.That(_ctrl.IsUnloaded, Is.True);
        }

        // --------------- OnViewportLost ---------------

        [Test]
        public void OnViewportLost_WhenReady_TransitionsToWaitingForViewport()
        {
            // Given
            SetState(GridLifecycleState.Ready);

            // When
            _ctrl.OnViewportLost();

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.WaitingForViewport));
            Assert.That(_ctrl.NeedsViewport, Is.True);
        }

        [Test]
        public void OnViewportLost_WhenInitializing_TransitionsToWaitingForViewport()
        {
            // Given
            SetState(GridLifecycleState.Initializing);

            // When
            _ctrl.OnViewportLost();

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.WaitingForViewport));
        }

        [Test]
        public void OnViewportLost_WhenTemplateReady_IsNoOp()
        {
            // Given
            _ctrl.OnTemplateApplied();

            // When
            _ctrl.OnViewportLost();

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.TemplateReady));
        }

        // --------------- OnGridUnloaded / OnGridLoaded ---------------

        [Test]
        public void OnGridUnloaded_TransitionsToUnloaded()
        {
            // Given
            SetState(GridLifecycleState.Ready);

            // When
            _ctrl.OnGridUnloaded();

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.Unloaded));
            Assert.That(_ctrl.IsUnloaded,     Is.True);
            Assert.That(_ctrl.CanHandleInput, Is.False);
        }

        [Test]
        public void OnGridLoaded_AfterUnloaded_TransitionsToTemplateReady()
        {
            // Given
            _ctrl.OnGridUnloaded();

            // When
            _ctrl.OnGridLoaded();

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.TemplateReady));
            Assert.That(_ctrl.IsUnloaded, Is.False);
        }

        [Test]
        public void OnGridLoaded_WhenNotUnloaded_IsNoOp()
        {
            // Given — grid was never unloaded
            _ctrl.OnTemplateApplied();

            // When
            _ctrl.OnGridLoaded();

            // Then
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.TemplateReady));
        }

        // --------------- Full lifecycle roundtrip ---------------

        [Test]
        public void FullRoundtrip_UnloadAndReload_RestoresTemplateReady()
        {
            // Given — grid fully initialized
            SetState(GridLifecycleState.Ready);

            // When — unload, DataSource binding fires (TryInitialize), then reload
            _ctrl.OnGridUnloaded();
            _ctrl.BeginReinitialize();                                    // no-op while Unloaded
            _ctrl.OnInitializationComplete(canvasReady: true, hasData: true); // no-op while Unloaded
            _ctrl.OnGridLoaded();

            // Then — OnGridLoaded correctly finds IsUnloaded=true and transitions
            Assert.That(_ctrl.State, Is.EqualTo(GridLifecycleState.TemplateReady));
            Assert.That(_ctrl.IsUnloaded, Is.False);
        }

        // --------------- Guards orthogonality ---------------

        [Test]
        public void Guards_OnlyReadyStateActivatesCanHandleInputAndIsInitialized()
        {
            foreach (GridLifecycleState s in System.Enum.GetValues(typeof(GridLifecycleState)))
            {
                SetState(s);
                bool expected = s == GridLifecycleState.Ready;
                Assert.That(_ctrl.CanHandleInput, Is.EqualTo(expected), $"CanHandleInput wrong for {s}");
                Assert.That(_ctrl.IsInitialized,  Is.EqualTo(expected), $"IsInitialized wrong for {s}");
            }
        }

        [Test]
        public void Guards_OnlyUnloadedStateActivatesIsUnloaded()
        {
            foreach (GridLifecycleState s in System.Enum.GetValues(typeof(GridLifecycleState)))
            {
                SetState(s);
                Assert.That(_ctrl.IsUnloaded, Is.EqualTo(s == GridLifecycleState.Unloaded), $"IsUnloaded wrong for {s}");
            }
        }

        [Test]
        public void Guards_OnlyWaitingForViewportActivatesNeedsViewport()
        {
            foreach (GridLifecycleState s in System.Enum.GetValues(typeof(GridLifecycleState)))
            {
                SetState(s);
                Assert.That(_ctrl.NeedsViewport, Is.EqualTo(s == GridLifecycleState.WaitingForViewport), $"NeedsViewport wrong for {s}");
            }
        }

        // --------------- Helpers ---------------

        // Bypasses transition guards to place controller in an arbitrary state for test setup.
        private void SetState(GridLifecycleState target)
        {
            _ctrl = new GridLifecycleController();
            switch (target)
            {
                case GridLifecycleState.NoTemplate:
                    break;
                case GridLifecycleState.TemplateReady:
                    _ctrl.OnTemplateApplied();
                    break;
                case GridLifecycleState.WaitingForViewport:
                    _ctrl.OnTemplateApplied();
                    _ctrl.OnInitializationComplete(canvasReady: false, hasData: true);
                    break;
                case GridLifecycleState.Initializing:
                    _ctrl.OnTemplateApplied();
                    _ctrl.OnAsyncInitStarted();
                    break;
                case GridLifecycleState.Ready:
                    _ctrl.OnTemplateApplied();
                    _ctrl.OnInitializationComplete(canvasReady: true, hasData: true);
                    break;
                case GridLifecycleState.Unloaded:
                    _ctrl.OnGridUnloaded();
                    break;
            }
        }
    }
}
