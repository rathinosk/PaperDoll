// The inputs pushed to one parameter during an update (by animation lanes) and how they merge with the host's value:
// additive inputs add, multiplicative and weighted ones multiply (averaged over the inputs), and a forced one replaces
// the value. Kept as running sums, so pushing an input allocates nothing.

using System.Numerics;

namespace PaperDoll.Evaluation;

/// <summary>
/// The inputs pushed to one parameter of one instance before it is evaluated (by animation lanes), merged with the
/// host's value:
/// <c>(value + Σ additive) * (Σ multiplicative_i * weight_i) / count</c>, where the multiplier is 1 with no
/// multiplicative or weighted inputs. Forced inputs replace the value itself. Inputs are cleared after each evaluation.
/// </summary>
/// <remarks>
/// KLUDGE: the weighted sum is divided by the number of inputs, not by the sum of the weights, as nijilive divides it.
/// An input for one axis leaves the other axis unchanged (docs/design/nijilive-differences.md).
/// </remarks>
internal struct ParameterInputs
{
    private Vector2 _sum;
    private Vector2 _product;
    private int _productCount;

    /// <summary>Pushes an input for both axes. <see cref="ParameterMergeMode.Passthrough"/> uses the parameter's own mode.</summary>
    /// <param name="value">The input.</param>
    /// <param name="mode">How it merges.</param>
    /// <param name="parameterMode">The parameter's merge mode, used for <see cref="ParameterMergeMode.Passthrough"/>.</param>
    /// <param name="weight">The weight of a <see cref="ParameterMergeMode.Weighted"/> input.</param>
    /// <param name="hostValue">The parameter's value, replaced by a <see cref="ParameterMergeMode.Forced"/> input.</param>
    public void Push(Vector2 value, ParameterMergeMode mode, ParameterMergeMode parameterMode, float weight, ref Vector2 hostValue)
    {
        if (mode == ParameterMergeMode.Passthrough)
            mode = parameterMode;
        switch (mode)
        {
            case ParameterMergeMode.Forced:
                hostValue = value;
                break;
            case ParameterMergeMode.Additive:
                _sum += value;
                break;
            case ParameterMergeMode.Multiplicative:
                AddProduct(value, 1f);
                break;
            case ParameterMergeMode.Weighted:
                AddProduct(value, weight);
                break;
        }
    }

    /// <summary>Pushes an input for one axis (0 for X, 1 for Y); see <see cref="Push"/>.</summary>
    public void PushAxis(int axis, float value, ParameterMergeMode mode, ParameterMergeMode parameterMode, float weight, ref Vector2 hostValue)
    {
        if (mode == ParameterMergeMode.Passthrough)
            mode = parameterMode;
        if (mode == ParameterMergeMode.Forced)
        {
            hostValue[axis] = value;
            return;
        }

        // nijilive fills the other axis with 1 for every kind of input, so an additive animation lane on one axis of a 2D
        // parameter also moves the other axis by 1. PaperDoll fills it with what leaves it unchanged: 0 added, 1 multiplied
        // (docs/design/nijilive-differences.md).
        var other = mode == ParameterMergeMode.Additive ? 0f : 1f;
        var full = new Vector2(axis == 0 ? value : other, axis == 1 ? value : other);
        Push(full, mode, parameterMode, weight, ref hostValue);
    }

    /// <summary>The merged value, <c>(value + Σ additive) * average multiplier</c>.</summary>
    public readonly Vector2 Resolve(Vector2 hostValue) =>
        (hostValue + _sum) * (_productCount == 0 ? Vector2.One : _product / _productCount);

    /// <summary>Clears every input; done after each update.</summary>
    public void Clear() => this = default;

    private void AddProduct(Vector2 value, float weight)
    {
        _product += value * weight;
        _productCount++;
    }
}
