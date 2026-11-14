# Yetta Desktop 0.3.1-preview

Первый интерфейсный прототип Yetta на C# / .NET 10 / WPF. Страница [README проекта](../../README.md) содержит актуальные инструкции по загрузке и сборке.

Разделы: **Главная, Информация, Консоль, DAX Guard, DAX Logs, DAX Recovery, Настройки, Источники**.

- Внутренняя Marvine не выполняет CMD, PowerShell и системные команды, только собственный список действий.
- Windows Terminal и Windows PowerShell представлены встроенным текстовым PowerShell-хостом без повышения прав, если выбран профиль **User** и пользователь принял условия. Это не полноценный ConPTY.
- Административная PowerShell заблокирована, пока не реализован и не проверен настоящий DAX-брокер повышения прав. Подделывать токены нельзя.
- Guest, Safe User и User — логические политики приложения. Они не дают доступа к защищённым ресурсам Windows и не заменяют UAC.
- Admin, System, DAX Process, DAX Logs и DAX Recovery пока не реализованы. Центр активности хранит локальные события только в памяти текущего процесса.
- Интерфейсные темы Fluent и Glass не изменяют оформление самой Windows.

Локальные настройки: `%LOCALAPPDATA%\Yetta\settings.json`; нет сетевого сбора телеметрии.

## Сборка

В Windows PowerShell из корня проекта:

```powershell
.\scripts\prepare-fonts.ps1
dotnet run --project src/Yetta.Desktop/Yetta.Desktop.csproj
```

Публикация EXE делается автоматически через `.github/workflows/desktop-build.yml`. Компиляция тестируется на Windows GitHub Actions, ручные тесты UI на Windows 11 всё ещё необходимы.

Файлы программы распространяются под MIT; встроенный оригинальный Inter под SIL OFL. Дополнительные зависимости стороннего UI-кода не используются.
