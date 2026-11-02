# Segurança do Achievements MP

## O que o app protege

- **Chave da Steam Web API:** cifrada pelo Windows (DPAPI, usuário atual). Uma cópia do banco ou do backup não serve
  em outro computador ou usuário. A chave só é enviada para `api.steampowered.com`, por HTTPS, e o app não segue
  redirecionamentos, para ela não ser levada a outro endereço. Ela nunca vai para mensagens de erro nem para os logs.
- **Login do Xbox:** feito no navegador, no site da Microsoft, pela biblioteca oficial (MSAL, com PKCE). O app nunca vê a
  senha. O token de renovação fica cifrado pelo Windows (DPAPI) como a chave da Steam, e os tokens só vão para os
  servidores da Microsoft e do Xbox, por HTTPS, sem seguir redirecionamentos. Eles nunca vão para mensagens de erro nem
  para os logs. O app pede só a permissão de login do Xbox Live e só lê.
- **Sem chave embutida:** cada pessoa usa a própria chave; o executável não contém segredo nenhum. O ID do app na
  Microsoft (`XboxApp.ClientId`) não é segredo: app de desktop não tem segredo, e esse ID aparece na tela de login.
- **Dados vindos do banco ou de um backup:** links de guia só abrem se forem `http`/`https`; ícones só carregam por
  `https`. Um backup adulterado não consegue fazer o app abrir programas ou acessar pastas de rede. A foto de perfil
  guardada no banco só é aberta se for pequena (até 2 MB e 4096 px de lado), para uma imagem forjada não esgotar a
  memória a cada abertura do app.
- **Backup a restaurar:** é conferido antes (formato, integridade, versão) e só troca o banco na abertura seguinte,
  guardando uma cópia dos dados que estavam no app.

## O que o app não protege

- **Programas maliciosos rodando na sua conta do Windows.** Eles conseguem ler o que você consegue, inclusive a chave.
  Se suspeitar de algo, revogue a chave em <https://steamcommunity.com/dev/apikey> e gere outra, e tire o acesso do
  app à conta Microsoft em <https://account.live.com/consent/Manage>.
- **Executável sem assinatura digital.** O Windows mostra o aviso do SmartScreen na primeira instalação. Baixe o app só
  da página de Releases deste repositório.

## Atualizações

O app instalado procura versões novas nas Releases de `github.com/PABLOzxk/achievements-mp`. O pacote baixado é
conferido contra o hash publicado na mesma Release, o que protege contra download corrompido, mas não contra alguém que
controle a conta do GitHub do projeto. Por isso essa conta usa verificação em duas etapas, e as versões são
geradas só pelo workflow do próprio repositório, que testa instalação, atualização e desinstalação antes de cada envio.

## Como relatar um problema de segurança

Use **Security → Report a vulnerability** neste repositório, para o relato ficar privado até haver correção. Se a opção
não aparecer, abra uma issue pedindo contato, sem os detalhes.
