# Yetta Desktop

Первый интерфейсный прототип Yetta на C# / .NET 10 / WPF. Страница [README проекта](../../README.md) содержит актуальные инструкции по загрузке и сборке.

Разделы: **Главная, Информация, Консоль, DAX Guard, DAX Logs, DAX Recovery, Настройки, Источники**.

- Marvine выполняет собственные команды Yetta. Запуск Python-скриптов требует Python, активного профиля Administrator и подтверждения DAX.
- Guest и User — логические политики приложения. Старый Safe User автоматически переносится в Guest. Профили не дают доступа к защищённым ресурсам Windows и не заменяют UAC.
- System, полноценный DAX Process и DAX Recovery пока не реализованы. Центр активности хранит локальные события в пользовательском хранилище Yetta.
- Yetta использует одно оформление Windows 11 с системным акцентным цветом.

Локальные настройки: `%LOCALAPPDATA%\Yetta\settings.json`; нет сетевого сбора телеметрии.

## Сборка

В Windows PowerShell из корня проекта:

```powershell
dotnet run --project src/Yetta.Desktop/Yetta.Desktop.csproj
```

Публикация EXE делается автоматически через `.github/workflows/desktop-build.yml`. Компиляция тестируется на Windows GitHub Actions, ручные тесты UI на Windows 11 всё ещё необходимы.

Файлы программы распространяются под MIT. Интерфейс использует системный шрифт Windows. Прогресс обновления отображается внутри Yetta.
