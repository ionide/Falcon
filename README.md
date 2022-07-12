# Falcon

Wrapper around F# REPL (FSI) enabling better editor integration. It exposes internal state of FSI session such as defined types, or bounded values using embedded HTTP server. It can potentially be used as a base for implementing the richer and more integrated IDE experience around F# REPL.

### Current endpoints

- `/values` - returns the list of bounded values in format: `{ Name: string; Type: string; Value: string }`
- `/types` - returns the list of defined types in format `{Name: string; Signature: string}`. `Signature` should be nicely formatted type signature in style of Ionide's tooltips.

## How to Contribute

Ths project is hosted on [GitHub](https://github.com/ionide/Falcon) where you can [report issues](https://github.com/ionide/Falcon/issues), participate in [discussions](https://github.com/ionide/Falcon/discussions), fork
the project and submit pull requests.

### Building and Running

1. `dotnet tool restore`
2. `dotnet restore`
3. `dotnet run --project .\src\Falcon\`

### Imposter Syndrome Disclaimer

I want your help. _No really, I do_.

There might be a little voice inside that tells you you're not ready; that you need to do one more tutorial, or learn another framework, or write a few more blog posts before you can help me with this project.

I assure you, that's not the case.

And you don't just have to write code. You can help out by writing documentation, tests, or even by giving feedback about this work. (And yes, that includes giving feedback about the contribution guidelines.)

Thank you for contributing!

## Code of Conduct

Please note that this project is released with a [Contributor Code of Conduct](CODE_OF_CONDUCT.md). By participating in this project you agree to abide by its terms.

## Copyright

The library is available under [MIT license](LICENSE.md), which allows modification and redistribution for both commercial and non-commercial purposes.

## Our Sponsors

You can support Ionide development on [Open Collective](https://opencollective.com/ionide). 

### Partners

<div align="center">

<a href="https://lambdafactory.io"><img src="https://cdn-images-1.medium.com/max/332/1*la7_YvDFvrtA720P5bYWBQ@2x.png" alt="drawing" width="100"/></a>

</div>

### Sponsors

[Become a sponsor](https://opencollective.com/ionide) and get your logo on our README on Github, description in the VSCode marketplace and on [ionide.io](https://ionide.io) with a link to your site.

<div align="center">
    <a href="https://ionide.io/sponsors.html">
        <img src="https://opencollective.com/ionide/tiers/silver-sponsor.svg?avatarHeight=120&width=1000&button=false"/>
        <br/>
        <img src="https://opencollective.com/ionide/tiers/bronze-sponsor.svg?avatarHeight=120&width=1000&button=false"/>
    </a>
</div>
