# Публикация Orbiq V3.3.2 на GitHub

## Перед отправкой исходников

1. Открой эту папку `Orbiq` в VS Code.
2. Проверь проект: `dotnet restore`, затем `dotnet run`.
3. Выполни `git status` и убедись, что не добавляются личные сохранения, резервные ZIP, пользовательские аудио/изображения, токены или пароли.
4. Создай пустой публичный репозиторий `Orbiq` на GitHub. Не добавляй README/.gitignore на сайте: они уже есть локально.

Команды из терминала в папке проекта (замени `YOUR-USERNAME`):

```powershell
git init -b main
git add .
git status
git commit -m "Orbiq v3.3.2"
git remote add origin https://github.com/YOUR-USERNAME/Orbiq.git
git push -u origin main
```

## Сборка автономного EXE

```powershell
.\publish-win-x64.ps1
```

Проверь `publish\win-x64\Orbiq.exe` запуском на Windows. ZIP для GitHub Release будет создан в `artifacts\Orbiq-v3.3.2-win-x64.zip`.

## Создание Release

На странице репозитория открой **Releases → Draft a new release**. Создай тег `v3.3.2`, название `Orbiq v3.3.2`, прикрепи ZIP из `artifacts` и опубликуй релиз.

Перед публикацией проверь готовый EXE на Windows. Сборку нужно тестировать отдельно от VS Code. Не проси пользователей отключать SmartScreen или другие средства защиты.

## Приватные данные

`.gitignore` исключает стандартные результаты сборки и распространённые локальные файлы данных, но всё равно проверяй `git status` перед `git add .` и перед `git push`. Не загружай пользовательские сохранения, резервные копии, личные медиафайлы, ключи или пароли.
