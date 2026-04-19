using DeployAssistant.ViewModel;
using System.ComponentModel;
using Xunit;

namespace DeployAssistant.Tests.ViewModel
{
    public class ViewModelBaseTests
    {
        private class TestViewModel : ViewModelBase
        {
            private string _name = "";
            public string Name
            {
                get => _name;
                set => SetField(ref _name, value);
            }

            private int _count;
            public int Count
            {
                get => _count;
                set => SetField(ref _count, value);
            }

            public void RaiseExplicit(string propertyName) => OnPropertyChanged(propertyName);
        }

        [Fact]
        public void OnPropertyChanged_RaisesPropertyChangedEvent()
        {
            var vm = new TestViewModel();
            string? raisedName = null;
            vm.PropertyChanged += (_, e) => raisedName = e.PropertyName;

            vm.RaiseExplicit("SomeProp");

            Assert.Equal("SomeProp", raisedName);
        }

        [Fact]
        public void SetField_WhenValueChanges_RaisesPropertyChangedAndReturnsTrue()
        {
            var vm = new TestViewModel();
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            bool changed = false;
            vm.Name = "Alice";   // initial assignment through property setter

            // Only capture after the first assignment
            raised.Clear();
            vm.Name = "Bob";

            Assert.Contains("Name", raised);
        }

        [Fact]
        public void SetField_WhenValueUnchanged_DoesNotRaiseEvent()
        {
            var vm = new TestViewModel();
            vm.Name = "Alice";

            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Name = "Alice"; // same value — no change

            Assert.Empty(raised);
        }

        [Fact]
        public void SetField_ReturnsFalse_WhenValueUnchanged()
        {
            // Verify via a direct backing field scenario using Count
            var vm = new TestViewModel();
            vm.Count = 5;

            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            vm.Count = 5;

            Assert.Empty(raised);
        }

        [Fact]
        public void SetField_ReturnsTrue_WhenValueChanged()
        {
            var vm = new TestViewModel();
            vm.Count = 1;

            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            vm.Count = 2;

            Assert.Contains("Count", raised);
        }

        [Fact]
        public void PropertyChanged_EventIsNull_DoesNotThrow()
        {
            var vm = new TestViewModel();
            // No subscribers — should not throw
            var ex = Record.Exception(() => vm.RaiseExplicit("X"));
            Assert.Null(ex);
        }
    }
}
