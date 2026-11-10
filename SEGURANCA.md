# Segurança do Achievements MP

## O que o app protege

- **Chave da Steam Web API:** cifrada pelo Windows (DPAPI, usuário atual). Uma cópia do banco ou do backup não serve
  em outro computador ou usuário. A chave só é enviada para `api.steampowered.com`, por HTTPS, e o app não segue
  redirecionamentos, para ela não ser levada a outro endereço. Ela nunca vai para mensagens de erro nem para os logs.
- **Token da Família Steam:** a importação única pede o `webapi_token` da sua sessão na loja, porque a Steam não
  aceita a chave da Web API para ler a família. Esse token dá acesso à conta enquanto vale (cerca de um dia), então o
  app trata como senha: campo oculto, usado em duas chamadas a `api.steampowered.com` por HTTPS sem redirecionamento,
  nunca salvo no banco, no backup ou nos logs, e apagado do campo assim que a importação começa. Antes de enviar, o
  app confere no próprio token se ele é da conta configurada e se ainda vale. A detecção automática de jogos
  emprestados não usa token: só a chave.
- **Sem chave embutida:** cada pessoa usa a própria chave; o executável não contém segredo nenhum.
- **Dados vindos do banco ou de um backup:** links de guia só abrem se forem `http`/`https`; ícones só carregam por
  `https`. Um backup adulterado não consegue fazer o app abrir programas ou acessar pastas de rede. A foto de perfil
  guardada no banco só é aberta se for pequena (até 2 MB e 4096 px de lado), para uma imagem forjada não esgotar a
  memória a cada abertura do app.
- **Backup a restaurar:** é conferido antes (formato, integridade, versão) e só troca o banco na abertura seguinte,
  guardando uma cópia dos dados que estavam no app.

## O que o app não protege

- **Programas maliciosos rodando na sua conta do Windows.** Eles conseguem ler o que você consegue, inclusive a chave.
  Se suspeitar de algo, revogue a chave em <https://steamcommunity.com/dev/apikey> e gere outra.
- **Token colado em outro lugar.** Pedir para alguém colar esse token é um jeito de roubar contas Steam. O app só pede
  em Configurações → Família Steam; ninguém do projeto vai pedir por mensagem. Se colou em lugar errado, troque a senha
  da Steam, o que encerra as sessões e invalida o token.
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
