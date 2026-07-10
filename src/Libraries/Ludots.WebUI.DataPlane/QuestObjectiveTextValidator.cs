namespace Ludots.WebUI.DataPlane;

/// <summary>
/// Validates objective copy for WebUI projection.
/// Plain <c>ObjectiveText</c> is accepted when no token is declared.
/// When <c>ObjectiveTextToken</c> is declared, a PresentationText token resolver hook is required
/// (WPK-5 localization catalog). Missing text or token fails with concrete quest/stage/token ids.
/// </summary>
public interface IQuestObjectiveTextValidator
{
	void Validate(
		string questId,
		string stageId,
		string objectiveText,
		string objectiveTextToken);
}

public sealed class QuestObjectiveTextValidator : IQuestObjectiveTextValidator
{
	private readonly Func<string, bool>? _isTextTokenRegistered;

	/// <param name="isTextTokenRegistered">
	/// Optional WPK-5 hook. When null, stages may only use plain <c>ObjectiveText</c>;
	/// any non-empty <c>ObjectiveTextToken</c> fails fast instead of silently ignoring the token.
	/// </param>
	public QuestObjectiveTextValidator(Func<string, bool>? isTextTokenRegistered = null)
	{
		_isTextTokenRegistered = isTextTokenRegistered;
	}

	public void Validate(
		string questId,
		string stageId,
		string objectiveText,
		string objectiveTextToken)
	{
		if (string.IsNullOrWhiteSpace(questId))
		{
			throw new ArgumentException("Quest id is required.", nameof(questId));
		}

		if (string.IsNullOrWhiteSpace(stageId))
		{
			throw new ArgumentException("Stage id is required.", nameof(stageId));
		}

		if (!string.IsNullOrWhiteSpace(objectiveTextToken))
		{
			string token = objectiveTextToken.Trim();
			if (_isTextTokenRegistered == null)
			{
				throw new InvalidOperationException(
					$"Quest '{questId}' stage '{stageId}' declares objective text token '{token}', " +
					"but no PresentationText token resolver hook is configured (WPK-5 dependency).");
			}

			if (!_isTextTokenRegistered(token))
			{
				throw new InvalidOperationException(
					$"Quest '{questId}' stage '{stageId}' objective text token '{token}' is not registered.");
			}

			return;
		}

		if (string.IsNullOrWhiteSpace(objectiveText))
		{
			throw new InvalidOperationException(
				$"Quest '{questId}' stage '{stageId}' is missing objective text and objective text token.");
		}
	}
}
