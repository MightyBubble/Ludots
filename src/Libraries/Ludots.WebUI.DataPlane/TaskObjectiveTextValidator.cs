namespace Ludots.WebUI.DataPlane;

/// <summary>
/// Validates objective copy for WebUI projection.
/// Plain objective text is accepted when no token is declared.
/// When a text token is declared, a PresentationText token resolver hook is required
/// (WPK-5 localization catalog). Missing text or token fails with concrete task/objective/token ids.
/// </summary>
public interface ITaskObjectiveTextValidator
{
	void Validate(
		string taskId,
		string objectiveId,
		string objectiveText,
		string objectiveTextToken);
}

public sealed class TaskObjectiveTextValidator : ITaskObjectiveTextValidator
{
	private readonly Func<string, bool>? _isTextTokenRegistered;

	/// <param name="isTextTokenRegistered">
	/// Optional WPK-5 hook. When null, objectives may only use plain text;
	/// any non-empty text token fails fast instead of silently ignoring the token.
	/// </param>
	public TaskObjectiveTextValidator(Func<string, bool>? isTextTokenRegistered = null)
	{
		_isTextTokenRegistered = isTextTokenRegistered;
	}

	public void Validate(
		string taskId,
		string objectiveId,
		string objectiveText,
		string objectiveTextToken)
	{
		if (string.IsNullOrWhiteSpace(taskId))
		{
			throw new ArgumentException("Task id is required.", nameof(taskId));
		}

		if (string.IsNullOrWhiteSpace(objectiveId))
		{
			throw new ArgumentException("Objective id is required.", nameof(objectiveId));
		}

		if (!string.IsNullOrWhiteSpace(objectiveTextToken))
		{
			string token = objectiveTextToken.Trim();
			if (_isTextTokenRegistered == null)
			{
				throw new InvalidOperationException(
					$"Task '{taskId}' objective '{objectiveId}' declares objective text token '{token}', " +
					"but no PresentationText token resolver hook is configured (WPK-5 dependency).");
			}

			if (!_isTextTokenRegistered(token))
			{
				throw new InvalidOperationException(
					$"Task '{taskId}' objective '{objectiveId}' objective text token '{token}' is not registered.");
			}

			return;
		}

		if (string.IsNullOrWhiteSpace(objectiveText))
		{
			throw new InvalidOperationException(
				$"Task '{taskId}' objective '{objectiveId}' is missing objective text and objective text token.");
		}
	}
}
