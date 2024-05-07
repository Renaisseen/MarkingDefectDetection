using Telegram.Bot; //Telegram bot + BotFather helper
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using System;
using System.Linq;
using System.Text;
using Telegram.Bot.Polling;
using Telegram.Bot.Exceptions;
using CableDefect.OCR;
using Telegram.Bot.Types.ReplyMarkups;
using System.Data.SQLite; //SQLite
using ZXing; //Zebra Сrossing for QR recognize
using ZXing.Common;
using System.Drawing;

namespace CableDefect.Interface
{
    public static class TelegramHandler
    {
        #region Globals
        //private static string _destinationFilePath = @"E:/FreeZone/KNU/Cable Defect AI detector/Files/";
        private static string _destinationFilePath = @"E:/FreeZone/KNU/Cable Defect AI detector/Cable Defect AI/Cable Defect AI/Files/";
        //private static string _destinationQRPhotosPath = @"E:/FreeZone/KNU/Cable Defect AI detector/QR/";
        private static string _destinationQRPhotosPath = @"E:/FreeZone/KNU/Cable Defect AI detector/Cable Defect AI/Cable Defect AI/QR/";
        //private static string _connectionString = @"Data Source=E:\FreeZone\KNU\Cable Defect AI detector\DB\Resources.sqlite;Version=3;";
        private static string _connectionString = @"Data Source=E:\FreeZone\KNU\Cable Defect AI detector\Cable Defect AI\Cable Defect AI\DB\Resources.sqlite;Version=3;";

        private const string _menuCommandSetResource = "/set_resource";
        private const string _menuCommandReset = "/reset";
        private const string _menuCommandInfo = "/help";
        private const string _menuCommandStart = "/start";

        private const string _firstTimeMessage = $"1. Please choose produced resource by running {_menuCommandSetResource} command and sending QR-code of respective resource.\n2. Send a picture of cable marking text for analysis.\n3. We'll recognize text and compare it with the right resource marking.\nFor detailed info: {_menuCommandInfo}";

        private const string _afterSelecterResourceMessage = $"1.You can send a picture of cable marking text for analysis.\n3. We'll recognize text and compare it with the right resource marking.\nIf you want to change resource use {_menuCommandSetResource}.\nFor detailed info: {_menuCommandInfo}.\nWaiting for marking photo...";

        private const string _infoMessage = $"This tool aims for defects detection for some resource within resource classifier.\nFirst of all, you need to choose resource by sending photo of resource QR-code (from the screen of production task or from printed QR-code list) to this chat.\nAfter that you can send pictures of output sheathed cable marking.\nMini-instruction:\n1. Start the process of setting required resource to be analyzed by running {_menuCommandSetResource} command and sending QR-code of respective resource afterwards.\n2. Next, you can start sending pictures of cable marking text for analysis.\n3. We'll recognize text and compare it with the right resource marking within resources database. In case of marking being defective please contact quality assurance department!";

        private static TelegramBotClient _bot;
        private static SQLiteConnection _sqlConnection = new SQLiteConnection(_connectionString);
        #endregion

        public static void Start(string telegramToken)
        {
            _bot = new TelegramBotClient(telegramToken);

            ReceiverOptions receiverOptions = new()
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };

            _bot.StartReceiving(
                updateHandler: handleUpdateAsync,
                pollingErrorHandler: handlePollingErrorAsync,
                receiverOptions: receiverOptions
                //cancellationToken: cts.Token
            );

            _sqlConnection.Open();

            Console.WriteLine("Bot started. Press any key to exit.");
            Console.ReadKey();

            _sqlConnection.Close();

            // Stop the bot
            //_bot.StopReceiving();
        }

        async static Task handleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            // Only process Message updates: https://core.telegram.org/bots/api#message
            if (update.Message is not { } message)
                return;

            var chatId = message.Chat.Id;
            Console.WriteLine("chatId: " + chatId);

            //await clearMainMenuAsync(chatId);
            //await showInlineMainMenuAsync(chatId);
            //await showMainMenuAsync(chatId);

            if(update.Message?.Text == _menuCommandReset)
            {
                //setResourceByChatId(chatId, _sqlConnection, string.Empty);
                if (checkChatStateExists(chatId, _sqlConnection))
                {
                    deleteState(chatId, _sqlConnection);
                }
                var answerReset = $"Reset is done. You can start the process when you are ready.";
                await sendReply(chatId, cancellationToken, answerReset, update.Message.MessageId);
                return;
            }

            var chatStateExist = checkChatStateExists(chatId, _sqlConnection);
            if (!chatStateExist)
            {
                insertEmptyChatState(chatId, _sqlConnection);
                //await showMainMenuAsync(chatId);
            }

            var waitingResSet = checkWaitingResCode(chatId, _sqlConnection);
            if (waitingResSet)
            {
                if(update.Message?.Type != MessageType.Photo)
                {
                    var answer = $"Please send QR-code of output resource code! If you don't want to set resource code now please use {_menuCommandReset} command.";
                    await sendReply(chatId, cancellationToken, answer, update.Message.MessageId);
                    return;
                }

                var imageFile = update.Message.Photo.Last();
                var dateTimeNow = DateTime.Now;
                var imageName = chatId + "-" + dateTimeNow.ToShortDateString() + "-" + dateTimeNow.Hour + "h" + dateTimeNow.Minute + "m" + dateTimeNow.Second + "s" + ".png";
                var imageLocalPath = _destinationQRPhotosPath + imageName;
                await SaveFile(imageFile, imageLocalPath, cancellationToken);
                var resCodeQR = getTextFromQR(imageLocalPath, _sqlConnection);
                if (string.IsNullOrEmpty(resCodeQR))
                {
                    var answerError = $"No text was decoded from given QR-code. Please send photo again or use {_menuCommandReset} command for now and refer to IT department.";
                    await sendReply(chatId, cancellationToken, answerError, update.Message.MessageId);
                    return;
                }
                if (!checkResCodeExists(resCodeQR, _sqlConnection))
                {
                    var answerError = $"Decoded resource code from QR-code doesn't exists in the database. Please send photo again or use {_menuCommandReset} command for now and refer to IT department.";
                    await sendReply(chatId, cancellationToken, answerError, update.Message.MessageId);
                    return;
                }
                setResourceByChatId(chatId, _sqlConnection, resCodeQR);
                setWaitingState(chatId, _sqlConnection, 0);
                var resName = getResourceName(resCodeQR, _sqlConnection);
                var text = $"You can now start sending pictures of {resName} cable marking text for analysis.\nWe'll recognize text and compare it with the right resource marking within resources database. In case of marking being defective please contact quality assurance department!";
                await sendReply(chatId, cancellationToken, text, update.Message.MessageId);
                return;
            }

            var resourceCode = getResourceCodeByChatId(chatId, _sqlConnection);
            switch (update.Message?.Text)
            {
                case _menuCommandStart:
                    await sendGeneralInfoMessage(chatId, cancellationToken);
                    await showMainMenuAsync(chatId);
                    return;

                case _menuCommandInfo:
                    await sendGeneralInfoMessage(chatId, cancellationToken);
                    return;

                case _menuCommandReset:
                    //setResourceByChatId(chatId, _sqlConnection, string.Empty);
                    if (checkChatStateExists(chatId, _sqlConnection)) 
                    {
                        deleteState(chatId, _sqlConnection);
                    }
                    var answerReset = $"Reset is done. You can start the process when you are ready.";
                    await sendReply(chatId, cancellationToken, answerReset, update.Message.MessageId);
                    return;

                case _menuCommandSetResource:
                    var answerSetResource = $"Please send QR-code image to identify resource code. Waiting...";
                    await sendReply(chatId, cancellationToken, answerSetResource, update.Message.MessageId);
                    setWaitingState(chatId, _sqlConnection, 1);
                    return;
            }

            //if (update.Message?.Text == "/start") 
            //{
            //    await botClient.SendTextMessageAsync(
            //    chatId: chatId,
            //    text: "Please send picture of marking text for analysis",
            //    cancellationToken: cancellationToken);
            //    return;
            //}

            if(update.Message?.Type == MessageType.Photo)
            {
                var resource = getResourceCodeByChatId(chatId, _sqlConnection);
                Console.WriteLine("resource: " + resource);
                if (string.IsNullOrEmpty(resource))
                {
                    var text = $"1. Please choose produced resource by sending QR-code of respective resource using {_menuCommandSetResource} command.\n2. Afterwards you can start sending pictures of cable marking text for analysis.\n3. We'll recognize text and compare it with the right resource marking within resources database. In case of marking being defective please contact quality assurance department!";
                    await sendReply(chatId, cancellationToken, text, update.Message.MessageId);
                    return;
                }
                var resourceMarking = getMarkingByResource(resource, _sqlConnection);
                if (string.IsNullOrEmpty(resourceMarking))
                {
                    var text = $"Marking for selected resource is not yet set in the resource classifier. Please, refer to IT department!";
                    await sendReply(chatId, cancellationToken, text, update.Message.MessageId);
                    return;
                }
                if(update.Message.Photo == null)
                {
                    var text = $"Photo is undefined. Please send another photo sample!";
                    await sendReply(chatId, cancellationToken, text, update.Message.MessageId);
                    return;
                }
                var imageFile = update.Message.Photo.Last();
                var dateTimeNow = DateTime.Now;
                var imageName = chatId + "-" + dateTimeNow.ToShortDateString() + "-" + dateTimeNow.Hour + "h" + dateTimeNow.Minute + "m" + dateTimeNow.Second + "s" + ".png";
                var imageLocalPath = _destinationFilePath + imageName;
                await SaveFile(imageFile, imageLocalPath, cancellationToken);
                //await using Stream fileStream = System.IO.File.Create(imageLocalPath);
                //var file = await botClient.GetInfoAndDownloadFileAsync(
                //    fileId: imageFile.FileId,
                //    destination: fileStream,
                //    cancellationToken: cancellationToken);
                //fileStream.Close();

                var result = Recognition.Recognize(imageName, Recognition.RecognizeOptions.FullText, Recognition.PreprocessOptions.Full);
                Console.WriteLine("result.Text: " + result.Text);

                var nonDefect = resMarkContainsRecognized(resourceMarkingText: resourceMarking, recognizedText: result.Text);

                await sendResult(chatId, cancellationToken, update.Message.MessageId, nonDefect, resourceMarking, result.Text, result.Comment);

                return;
            }

            if(!waitingResSet && !string.IsNullOrEmpty(resourceCode))
            {
                await sendReply(chatId, cancellationToken, _afterSelecterResourceMessage, update.Message.MessageId);
                return;
            }

            await sendReply(chatId, cancellationToken, _firstTimeMessage, update.Message.MessageId);
            return;
            

            // Only process text messages
            //if (message.Text is not { } messageText)
            //    return;

            //Console.WriteLine($"Received a '{messageText}' message in chat {chatId}.");

            // Echo received message text
            //Message sentMessage = await botClient.SendTextMessageAsync(
            //    chatId: chatId,
            //    text: "You said:\n" + messageText,
            //    cancellationToken: cancellationToken);
        }

        #region TelegramMethods
        private static async Task showMainMenuAsync(ChatId chatId)
        {
            var options = new ReplyKeyboardMarkup(new[]
            {
                new[]
                {
                    new KeyboardButton(_menuCommandSetResource),
                    new KeyboardButton(_menuCommandReset),
                    new KeyboardButton(_menuCommandInfo)
                }
            });

            options.ResizeKeyboard = true;

            await _bot.SendTextMessageAsync(chatId, $"Please select disired menu point...", replyMarkup: options);
        }
        [Obsolete("Not useful, since chat would be spammed with photos, menu would just disappear from the user's view")]
        private static async Task showInlineMainMenuAsync(ChatId chatId)
        {
            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new []
                {
                    InlineKeyboardButton.WithCallbackData("Set resource", _menuCommandSetResource),
                    InlineKeyboardButton.WithCallbackData("Reset", _menuCommandReset),
                    InlineKeyboardButton.WithCallbackData("Info", _menuCommandInfo),
                }
            });

            await _bot.SendTextMessageAsync(chatId, _menuCommandInfo, replyMarkup: inlineKeyboard);
        }
        private static async Task clearMainMenuAsync(ChatId chatId)
        {
            var menuMessage = "Welcome! Please choose an option:";
            var emptyKeyboard = new ReplyKeyboardRemove();

            await _bot.SendTextMessageAsync(chatId, menuMessage, replyMarkup: emptyKeyboard);
        }
        private static async Task sendGeneralInfoMessage(ChatId chatId, CancellationToken cancellationToken)
        {
            await _bot.SendTextMessageAsync(
                chatId: chatId,
                text: _infoMessage,
                cancellationToken: cancellationToken);
            return;
        }
        private static async Task sendReply(ChatId chatId, CancellationToken cancellationToken, string message, int? replyToMessageId = null)
        {
            await _bot.SendTextMessageAsync(
                chatId: chatId,
                text: message,
                cancellationToken: cancellationToken,
                replyToMessageId: replyToMessageId);
            return;
        }
        private static async Task sendResult(ChatId chatId, CancellationToken cancellationToken, int replyToMessageId, bool success, string resourceMarking, string recognizedText, string resultDetails = "")
        {
            var message = string.Empty;
            if (success)
            {
                message = $"✅ <b>Valid marking!</b> \nResource marking: {resourceMarking}\nRecognized text: {recognizedText}\n";
                if (!string.IsNullOrEmpty(resultDetails))
                {
                    message += $"\n---Details---\n {resultDetails}";
                }
            }
            else
            {
                message = $"⚠️ <b>Possible defect!</b> \nResource marking: {resourceMarking}\nRecognized text: {recognizedText}\n";
                if (!string.IsNullOrEmpty(resultDetails))
                {
                    message += $"\n---Details---\n {resultDetails}";
                }
            }
            await _bot.SendTextMessageAsync(
                chatId: chatId,
                text: message,
                cancellationToken: cancellationToken,
                replyToMessageId: replyToMessageId,
                parseMode: ParseMode.Html);
            return;
        }
        private static async Task SaveFile(PhotoSize imageFile, string directPathWithName, CancellationToken cancellationToken)
        {
            await using Stream fileStream = System.IO.File.Create(directPathWithName);
            var file = await _bot.GetInfoAndDownloadFileAsync(
            fileId: imageFile.FileId,
                destination: fileStream,
                cancellationToken: cancellationToken);
            fileStream.Close();
        }
        private static Task handlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;
        }
        #endregion

        #region Database methods
        /// <summary>
        /// update/insert chatId + current resource code
        /// </summary>
        /// <param name="chatId"></param>
        /// <param name="sqlConnection"></param>
        /// <param name="resCode"></param>
        /// <returns>number of rows updated/inserted. To check if okay check for '>0' </returns>
        private static int setResourceByChatId(ChatId chatId, SQLiteConnection sqlConnection, string? resCode, int waitingStateForInsert = 0)
        {
            var res = 0;
            var sqlStateExistsQuery = "select 1 from CHATSTATE where CHATSTATE.CHATID = @chatId";
            using (var stateExistsCommand = new SQLiteCommand(sqlStateExistsQuery, sqlConnection))
            {
                stateExistsCommand.Parameters.AddWithValue("chatId", chatId.Identifier);
                var exists = Convert.ToInt32(stateExistsCommand.ExecuteScalar()) == 1;
                if (exists)
                {
                    //update
                    var sqlStateUpdateQuery = "update CHATSTATE set RESCODE = @rescode where CHATSTATE.CHATID = @chatId";
                    var updCommand = new SQLiteCommand(sqlStateUpdateQuery, sqlConnection);
                    updCommand.Parameters.AddWithValue("chatId", chatId.Identifier);
                    updCommand.Parameters.AddWithValue("rescode", resCode);
                    res = updCommand.ExecuteNonQuery();
                }
                else
                {
                    //insert
                    var sqlStateInsertQuery = "insert into CHATSTATE (CHATID, RESCODE, WAITINGRESSET) values (@chatId, @rescode, @wait)";
                    var insCommand = new SQLiteCommand(sqlStateInsertQuery, sqlConnection);
                    insCommand.Parameters.AddWithValue("chatId", chatId.Identifier);
                    insCommand.Parameters.AddWithValue("rescode", resCode);
                    insCommand.Parameters.AddWithValue("wait", waitingStateForInsert);
                    res = insCommand.ExecuteNonQuery();
                }
            }
            return res;
        }
        private static int insertEmptyChatState(ChatId chatId, SQLiteConnection sqlConnection)
        {
            //insert key + empty non-nullable columns
            var sqlStateInsertQuery = "insert into CHATSTATE (CHATID, RESCODE, WAITINGRESSET) values (@chatId, '', 0)";
            var insCommand = new SQLiteCommand(sqlStateInsertQuery, sqlConnection);
            insCommand.Parameters.AddWithValue("chatId", chatId.Identifier);
            return insCommand.ExecuteNonQuery();
        }
        private static int setWaitingState(ChatId chatId, SQLiteConnection sqlConnection, int waitingState)
        {
            var sqlStateExistsQuery = "select 1 from CHATSTATE where CHATSTATE.CHATID = @chatId";
            using (var stateExistsCommand = new SQLiteCommand(sqlStateExistsQuery, sqlConnection))
            {
                stateExistsCommand.Parameters.AddWithValue("chatId", chatId.Identifier);
                var exists = Convert.ToInt32(stateExistsCommand.ExecuteScalar()) == 1;
                if (exists)
                {
                    //update
                    var sqlStateUpdateQuery = "update CHATSTATE set WAITINGRESSET = @waitingState where CHATSTATE.CHATID = @chatId";
                    var updCommand = new SQLiteCommand(sqlStateUpdateQuery, sqlConnection);
                    updCommand.Parameters.AddWithValue("chatId", chatId.Identifier);
                    updCommand.Parameters.AddWithValue("waitingState", waitingState);
                    return updCommand.ExecuteNonQuery();
                }
                else
                {
                    return 0;
                }
            }
        }
        private static bool deleteState(ChatId chatId, SQLiteConnection sqlConnection)
        {
            var sqlStateDeleteQuery = "delete from CHATSTATE where CHATSTATE.CHATID = @chatId";
            using (var stateDeleteCommand = new SQLiteCommand(sqlStateDeleteQuery, sqlConnection))
            {
                stateDeleteCommand.Parameters.AddWithValue("chatId", chatId.Identifier);
                var deleted = Convert.ToInt32(stateDeleteCommand.ExecuteNonQuery()) > 0;
                return deleted;
            }
        }
        private static string? getResourceCodeByChatId(ChatId chatId, SQLiteConnection sqlConnection)
        {
            var sqlSelectRes = "select RESCODE from CHATSTATE where CHATSTATE.CHATID = @chatId";
            using (var command = new SQLiteCommand(sqlSelectRes, sqlConnection))
            {
                command.Parameters.AddWithValue("chatId", chatId.Identifier);
                var resCode = Convert.ToString(command.ExecuteScalar(System.Data.CommandBehavior.SingleResult));
                return resCode;
            }
        }
        private static string? getResourceCodeByMarking(string marking, SQLiteConnection sqlConnection)
        {
            var sqlSelectRes = "select RESCODE from CHATSTATE where CHATSTATE.MARKING = @marking";
            using (var command = new SQLiteCommand(sqlSelectRes, sqlConnection))
            {
                command.Parameters.AddWithValue("marking", marking);
                var resCode = Convert.ToString(command.ExecuteScalar(System.Data.CommandBehavior.SingleResult));
                return resCode;
            }
        }
        private static string? getResourceName(string resCode, SQLiteConnection sqlConnection, bool fullName = true)
        {
            var nameField = fullName ? "NAME_FULL" : "NAME_SHORT";
            var sqlSelectRes = @$"select {nameField} from CLASSIFIER where CLASSIFIER.RESCODE = @resCode";
            using (var command = new SQLiteCommand(sqlSelectRes, sqlConnection))
            {
                command.Parameters.AddWithValue("resCode", resCode);
                var resName = Convert.ToString(command.ExecuteScalar(System.Data.CommandBehavior.SingleResult));
                return resName;
            }
        }
        private static string? getMarkingByResource(string resource, SQLiteConnection sqlConnection)
        {
            var sqlSelectRes = "select MARKING from CLASSIFIER where CLASSIFIER.RESCODE = @resCode";
            using (var command = new SQLiteCommand(sqlSelectRes, sqlConnection))
            {
                command.Parameters.AddWithValue("resCode", resource);
                var marking = Convert.ToString(command.ExecuteScalar(System.Data.CommandBehavior.SingleResult));
                return marking;
            }
        }
        private static bool checkWaitingResCode(ChatId chatId, SQLiteConnection sqlConnection)
        {
            var sqlSelectRes = "select WAITINGRESSET from CHATSTATE where CHATSTATE.CHATID = @chatId";
            using (var command = new SQLiteCommand(sqlSelectRes, sqlConnection))
            {
                command.Parameters.AddWithValue("chatId", chatId.Identifier);
                var waiting = Convert.ToInt64(command.ExecuteScalar(System.Data.CommandBehavior.SingleResult)) == 1;
                return waiting;
            }
        }
        private static bool checkResCodeExists(string resCodeToFind, SQLiteConnection sqlConnection)
        {
            var sqlStateExistsQuery = "select 1 from CLASSIFIER where CLASSIFIER.RESCODE = @resCode";
            using (var stateExistsCommand = new SQLiteCommand(sqlStateExistsQuery, sqlConnection))
            {
                stateExistsCommand.Parameters.AddWithValue("resCode", resCodeToFind);
                var exists = Convert.ToInt32(stateExistsCommand.ExecuteScalar()) == 1;
                return exists;
            }
        }
        private static bool checkChatStateExists(ChatId chatId, SQLiteConnection sqlConnection)
        {
            var sqlStateExistsQuery = "select 1 from CHATSTATE where CHATSTATE.CHATID = @chatId";
            using (var stateExistsCommand = new SQLiteCommand(sqlStateExistsQuery, sqlConnection))
            {
                stateExistsCommand.Parameters.AddWithValue("chatId", chatId.Identifier);
                //var exists = Convert.ToInt32(stateExistsCommand.ExecuteScalar()) == 1;
                var exists = Convert.ToInt64(stateExistsCommand.ExecuteScalar()) == 1;
                return exists;
            }
        }
        #endregion

        #region QR handle
        private static string getTextFromQR(string imagePath, SQLiteConnection sqlConnection)
        {
            try
            {
                var reader = new BarcodeReaderGeneric
                {
                    AutoRotate = true,
                    TryInverted = true,
                    Options = new DecodingOptions
                    {
                        TryHarder = true,
                        PureBarcode = false,
                        PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                    }
                };

                using (var barcodeBitmap = (Bitmap)Image.FromFile(imagePath))
                {
                    LuminanceSource source;
                    source = new BitmapLuminanceSource(barcodeBitmap);
                    BinaryBitmap bitmap = new BinaryBitmap(new HybridBinarizer(source));
                    Result result = new MultiFormatReader().decode(bitmap);

                    if (result != null)
                    {
                        return result.Text;
                    }
                    else
                    {
                        return string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetResCodeFromQR ERROR: " + ex.Message);
                return string.Empty;
            }
        }
        #endregion
        
        #region Process result methods
        private static bool resMarkContainsRecognized(string resourceMarkingText, string recognizedText)
        {
            return recognizedText.Contains(resourceMarkingText);
        }
        #endregion
    }
}
