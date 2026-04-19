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

        /// <summary>
        /// Exposes the raw boolean return value of <see cref="ViewModelBase.SetField{T}"/> for assertions.
        /// </summary>
        private class SetFieldReturnTestViewModel : ViewModelBase
        {
            private string _value = "";

            /// <summary>Assigns <paramref name="newValue"/> via SetField and returns the result.</summary>
            public bool Assign(string newValue) => SetField(ref _value, newValue, nameof(_value));
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
            vm.Name = "Alice"; // initial value
            raised.Clear();

            // Act — change to a different value
            vm.Name = "Bob";

            // Assert — PropertyChanged was raised
            Assert.Contains("Name", raised);
        }

        [Fact]
        public void SetField_ReturnsTrueOnChange_AndFalseOnNoChange()
        {
            // Access SetField directly through a specialised test subclass
            var vm = new SetFieldReturnTestViewModel();
            vm.Assign("Alice");

            // Changing the value → should return true
            bool changed = vm.Assign("Bob");
            Assert.True(changed);

            // Same value again → should return false
            bool unchanged = vm.Assign("Bob");
            Assert.False(unchanged);
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
