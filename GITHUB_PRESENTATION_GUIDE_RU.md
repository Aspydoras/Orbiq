# Обновление оформления GitHub для Orbiq

В архиве находятся файлы, которые нужно добавить или заменить в корне локального проекта Orbiq.

## Содержимое

- `README.md` — новая главная страница на английском языке.
- `README_RU.md` — подробная русская версия.
- `docs/assets/orbiq-banner.png` — баннер для README.
- `docs/assets/orbiq-social-preview.png` — изображение для Social Preview репозитория GitHub (1280 × 640).

## Как применить

1. Распакуй архив в локальную папку проекта Orbiq.
2. Разреши замену `README.md` и `README_RU.md`.
3. Убедись, что новая папка `docs/assets` находится в корне проекта.
4. Открой баннер и проверь, что он отображается корректно.
5. В терминале выполни:

```powershell
git status
git diff --check
git diff -- README.md README_RU.md
```

6. Если всё выглядит правильно, отправь изменения:

```powershell
git add README.md README_RU.md docs/assets/orbiq-banner.png docs/assets/orbiq-social-preview.png
git commit -m "Improve Orbiq repository presentation"
git push
```

## Установить Social Preview

1. Открой `https://github.com/Aspydoras/Orbiq`.
2. Нажми **Settings**.
3. В разделе **General** найди **Social preview**.
4. Нажми **Edit** или **Upload an image**.
5. Выбери `docs/assets/orbiq-social-preview.png`.
6. Сохрани изменения.

## Рекомендуемые GitHub Topics

Добавь в блок About справа на странице репозитория подходящие темы:

`windows`, `desktop-app`, `wpf`, `dotnet`, `productivity`, `pomodoro`, `notes`, `reminders`, `calendar`, `desktop-utility`

## Важные замечания

- В README ссылка на имеющийся релиз `v3.3.2` оставлена как есть.
- Статус релиза (обычный или pre-release) меняется отдельно на странице GitHub Releases.
- Лицензия намеренно не добавляется: владелец проекта должен самостоятельно выбрать условия распространения.
- Скриншоты интерфейса не добавлены, поскольку для них нужны настоящие снимки приложения. Не стоит выдавать концептуальные иллюстрации за фактические скриншоты программы.
