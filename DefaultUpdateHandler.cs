using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TeleSharp;

/// <summary>
///     A very simple <see cref="IUpdateHandler" /> implementation
/// </summary>
public class DefaultUpdateHandler : IUpdateHandler
{
    private readonly Func<ITelegramBotClient, Exception, CancellationToken, Task> _errorHandler;
    private readonly Func<ITelegramBotClient, Update, CancellationToken, Task> _updateHandler;

    /// <summary>
    ///     Constructs a new <see cref="DefaultUpdateHandler" /> with the specified callback functions
    /// </summary>
    /// <param name="updateHandler">The function to invoke when an update is received</param>
    /// <param name="errorHandler">The function to invoke when an error occurs</param>
    public DefaultUpdateHandler(
        Func<ITelegramBotClient, Update, CancellationToken, Task> updateHandler,
        Func<ITelegramBotClient, Exception, CancellationToken, Task> errorHandler)
    {
        _updateHandler = updateHandler ?? throw new ArgumentNullException(nameof(updateHandler));
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
    }

    public Task HandleUpdate(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task HandleError(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public UpdateType[] AllowedUpdates { get; }

    /// <inheritdoc />
    public async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken
    )
    {
        await _updateHandler(botClient, update, cancellationToken);
    }

    /// <inheritdoc />
    public async Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        await _errorHandler(botClient, exception, cancellationToken);
    }
}