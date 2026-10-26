# Segurança do Achievements MP

## O que o app protege

- **Chave da Steam Web API:** cifrada pelo Windows (DPAPI, usuário atual). Uma cópia do banco ou do backup não serve
  em outro computador ou usuário. A chave só é enviada para `api.steampowered.com`, por HTTPS, e o app não segue
  redirecionamentos, para ela não ser levada a outro endereço. Ela nunca vai para mensagens de erro nem para os logs.
- **Sem chave embutida:** cada pessoa usa a própria chave; o executável não contém segredo nenhum.
- **Dados vindos do banco ou de um backup:** links de guia só abrem se forem `http`/`https`; ícones só carregam por
  `https`. Um backup adulterado não consegue fazer o app abrir programas ou acessar pastas de rede.
- **Backup a restaurar:** é conferido antes (formato, integridade, versão) e só troca o banco na abertura seguinte,
  guardando uma cópia dos dados que estavam no app.

## O que o app não protege

- **Programas maliciosos rodando na sua conta do Windows.** Eles conseguem ler o que você consegue, inclusive a chave.
  Se suspeitar de algo, revogue a chave em <https://steamcommunity.com/dev/apikey> e gere outra.
- **Executável sem assinatura digital.** O Windows mostra o aviso do SmartScreen na primeira instalação. Baixe o app só
  da página de Releases deste repositório.

## Atualizações

O app instalado procura versões novas nas Releases de `github.com/PABLOzxk/achievements-mp`. O pacote baixado é
conferido contra o hash publicado na mesma Release, o que protege contra download corrompido, mas não contra alguém que
controle a conta do GitHub do projeto. Por isso a conta usa verificação em duas etapas, e as versões são geradas só
pelo workflow do próprio repositório, que testa instalação, atualização e desinstalação antes de cada envio.

## Como relatar um problema de segurança

Use **Security → Report a vulnerability** neste repositório, para o relato ficar privado até haver correção. Não abra
uma issue pública com os detalhes.
