using System.ComponentModel;
using Sinos.Components;

namespace Sinos.Share.Test.Components;

public class NotificationObjectTest
{
    private class TestObject : NotificationObject
    {
        private string? _referenceProperty;
        public string? ReferenceProperty
        {
            get => this._referenceProperty;
            set => this.SetIfChanged(ref this._referenceProperty, value);
        }

        private int _valueProeprty;
        public int ValueProperty
        {
            get => this._valueProeprty;
            set => this.SetIfChanged(ref this._valueProeprty, ref value);
        }
    }

    /// <summary>
    /// イベントの通知先がない場合の変更確認
    /// </summary>
    [Fact]
    public void TestNoEventListner()
    {
        var obj = new TestObject();
        obj.ValueProperty = 2;
        obj.ReferenceProperty = "test";
    }

    /// <summary>
    /// 参照型プロパティの変更通知テスト
    /// </summary>
    [Fact]
    public void TestWithEventListenr1()
    {
        var actualChanges = new List<(object? sender, PropertyChangedEventArgs)>(1);

        void Changed(object? sender, PropertyChangedEventArgs e)
        {
            actualChanges.Add((sender, e));
        }

        var obj = new TestObject();
        obj.PropertyChanged += Changed;

        try
        {
            obj.ReferenceProperty = "test2";
        }
        finally
        {
            obj.PropertyChanged -= Changed;
        }

        Assert.Single(actualChanges);

        var (sender, e) = actualChanges[0];
        Assert.Equal(obj, sender);
        Assert.Equal(nameof(obj.ReferenceProperty), e.PropertyName);
    }

    /// <summary>
    /// 値型プロパティの変更通知テスト
    /// </summary>
    [Fact]
    public void TestWithEventListenr2()
    {
        var actualChanges = new List<(object? sender, PropertyChangedEventArgs)>(1);

        void Changed(object? sender, PropertyChangedEventArgs e)
        {
            actualChanges.Add((sender, e));
        }

        var obj = new TestObject();
        obj.PropertyChanged += Changed;

        try
        {
            obj.ValueProperty = 4;
        }
        finally
        {
            obj.PropertyChanged -= Changed;
        }

        Assert.Single(actualChanges);

        var (sender, e) = actualChanges[0];
        Assert.Equal(obj, sender);
        Assert.Equal(nameof(obj.ValueProperty), e.PropertyName);
    }
}
