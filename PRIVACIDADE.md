# Política de privacidade do Achievements MP

Última atualização: 26/10/2026.

O Achievements MP é um programa que roda só no seu computador. Ele não tem servidor próprio, não tem conta de
usuário, não coleta estatísticas de uso e não envia seus dados para o desenvolvedor.

## O que fica guardado no seu computador

Na pasta `%LocalAppData%\AchievementsMP`:

- o perfil Steam que você informou (link, nome personalizado ou SteamID64);
- o nome e o endereço da foto do seu perfil na Steam e, se você trocar no app, o nome e a foto que escolheu
  (a foto é guardada reduzida, a imagem original não é copiada);
- a sua chave da Steam Web API, cifrada pelo Windows (DPAPI), que só pode ser lida pela sua conta de usuário neste computador;
- a lista de jogos, conquistas, tempo de jogo e raridade que o app leu da Steam;
- o que você cria no app: listas, metas, classificações, anotações, checklist e links de guias;
- cópias de segurança feitas antes de atualizar o banco e registros de funcionamento (logs), apagados depois de 30 dias. Os logs não contêm a chave.

## Com quem o app se comunica

- **Steam** (`api.steampowered.com` e servidores de imagem da Steam): para ler sua biblioteca, suas conquistas, a
  raridade global e o nome e a foto do seu perfil. O app envia a sua chave e o seu SteamID, que é o que a Steam exige
  para responder. O uso dos dados pela Steam segue a política de privacidade da Valve.
- **GitHub** (`github.com` e `api.github.com`): ao abrir, o app consulta se há versão nova e, se houver, baixa a
  atualização. O GitHub vê o endereço IP de quem consulta, como em qualquer acesso ao site.
- **Links que você abre** (perfil, privacidade, guias) vão para o seu navegador.

## Backup

Se você escolher uma pasta de backup, o app grava lá uma cópia do banco uma vez por dia. Se a pasta for de um serviço
de nuvem (OneDrive, Google Drive), esse serviço guarda as cópias segundo as regras dele. A chave da Steam vai cifrada
no backup e não abre em outro computador ou usuário.

## Como apagar seus dados

- Para tirar a chave: Configurações → Remover.
- Para apagar tudo: desinstale o app e apague a pasta `%LocalAppData%\AchievementsMP` e, se usou, a pasta de backup.
- A chave também pode ser revogada a qualquer momento em <https://steamcommunity.com/dev/apikey>.

## Contato

Dúvidas ou problemas: abra uma issue em <https://github.com/PABLOzxk/achievements-mp/issues>.
