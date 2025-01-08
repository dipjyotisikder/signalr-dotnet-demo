﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SignalR.Api.Hubs.Services;
using SignalR.Api.MessagingModule.Models;
using SignalR.Api.UserModule.Services;
using SignalR.Api.UserModule.Models;
using SignalR.Api.MessagingModule.Models.Entities;
using SignalR.Api.Constants;

namespace SignalR.Api.MessagingModule.Services;

/// <summary>
/// Service for managing conversations.
/// </summary>
public class ConversationService : IConversationService
{
    private readonly IDataContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IHubService _hubService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationService"/> class.
    /// </summary>
    /// <param name="context">The data context.</param>
    /// <param name="currentUser">The current user.</param>
    /// <param name="hubService">The hub service.</param>
    public ConversationService(
        IDataContext context,
        ICurrentUser currentUser,
        IHubService hubService)
    {
        _context = context;
        _currentUser = currentUser;
        _hubService = hubService;
    }

    /// <summary>
    /// Creates a new conversation.
    /// </summary>
    /// <param name="request">The create conversation model.</param>
    /// <returns>The created conversation model.</returns>
    public async Task<ConversationModel> Create(CreateConversationModel request)
    {
        var currentUserId = _currentUser.Id;

        var conversation = new Conversation
        {
            Id = _context.Conversations.Count() + 1,
            CreatedAt = DateTime.UtcNow,
            Title = request.Title,
            CreatorUserId = currentUserId,
        };

        _context.Conversations.Add(conversation);

        _context.ConversationAudiences.Add(new ConversationAudience
        {
            ConversationId = conversation.Id,
            AudienceUserId = currentUserId
        });

        await _context.SaveChangesAsync();

        var creatorUser = _context.Users
            .Where(x => x.Id == conversation.CreatorUserId)
            .Select(x => new UserModel
            {
                Id = x.Id,
                FullName = x.FullName,
                PhotoUrl = x.PhotoUrl,
            }).FirstOrDefault();

        return new ConversationModel
        {
            Id = conversation.Id,
            CreatedAt = conversation.CreatedAt,
            Title = conversation.Title,
            CreatorUser = creatorUser
        };
    }

    /// <summary>
    /// Gets all conversations.
    /// </summary>
    /// <returns>A list of conversation models.</returns>
    public IEnumerable<ConversationModel> GetAll()
    {
        return from c in _context.Conversations
               join cu in _context.Users
               on c.CreatorUserId equals cu.Id into creatorUsers
               from cu in creatorUsers.DefaultIfEmpty()
               select new ConversationModel
               {
                   Id = c.Id,
                   Title = c.Title,
                   CreatedAt = c.CreatedAt,
                   CreatorUser = cu == null ? null : new UserModel
                   {
                       Id = cu.Id,
                       FullName = cu.FullName,
                       PhotoUrl = cu.PhotoUrl
                   }
               };
    }

    /// <summary>
    /// Gets the audiences of a conversation.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <returns>The conversation audience model.</returns>
    public async Task<ConversationAudienceModel> GetAudiences(int conversationId)
    {
        var conversation = _context.Conversations.FirstOrDefault(x => x.Id == conversationId);
        if (conversation == null)
        {
            return null;
        }

        // ADD CURRENT USER TO CONVERSATION
        var isAnAudience = _context.ConversationAudiences.Where(x => x.AudienceUserId == _currentUser.Id && x.ConversationId == conversation.Id).Any();
        if (!isAnAudience)
        {
            _context.ConversationAudiences.Add(new ConversationAudience
            {
                AudienceUserId = _currentUser.Id,
                ConversationId = conversation.Id
            });

            await _context.SaveChangesAsync();

            // SEND CONNECTING USER TO ALL WHO BELONG TO THIS CONVERSATION.
            var conversationAudiences = _context.ConversationAudiences
                .Where(x => x.ConversationId == conversationId)
                .Select(x => x.AudienceUserId.ToString()).Distinct();

            var payload = _context.Users
                .Select(x => new UserModel
                {
                    Id = x.Id,
                    FullName = x.FullName,
                    PhotoUrl = x.PhotoUrl
                }).First(x => x.Id == _currentUser.Id);

            await _hubService
                .SendToGroupsAsync(
                    groups: conversationAudiences,
                    eventName: HubEventName.Create(HubConstants.Events.USER_IS_JOINED),
                    payload: payload);
        }

        var audiences = from ca in _context.ConversationAudiences.Where(x => x.ConversationId == conversationId)
                        join au in _context.Users on ca.AudienceUserId equals au.Id
                        select new UserModel
                        {
                            Id = au.Id,
                            FullName = au.FullName,
                            PhotoUrl = au.PhotoUrl,
                        };

        var creatorUser = _context.Users
            .Where(x => x.Id == conversation.CreatorUserId)
            .Select(x => new UserModel
            {
                Id = x.Id,
                FullName = x.FullName,
                PhotoUrl = x.PhotoUrl,
            }).FirstOrDefault();

        return new ConversationAudienceModel
        {
            Id = conversation.Id,
            Title = conversation.Title,
            CreatedAt = conversation.CreatedAt,
            CreatorUser = creatorUser,
            AudienceUsers = audiences ?? Enumerable.Empty<UserModel>()
        };
    }

    /// <summary>
    /// Gets the messages of a conversation.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <returns>A list of message models.</returns>
    public IEnumerable<MessageModel> GetMessages(int conversationId)
    {
        var messages = from m in _context.Messages.Where(x => x.ConversationId == conversationId)
                       join cu in _context.Users on m.CreatorUserId equals cu.Id into creatorUsers
                       from cu in creatorUsers.DefaultIfEmpty()
                       select new MessageModel
                       {
                           Id = m.Id,
                           Text = m.Text,
                           CreatedAt = m.CreatedAt,
                           CreatorUser = cu == null ? null : new UserModel
                           {
                               Id = cu.Id,
                               FullName = cu.FullName,
                               PhotoUrl = cu.PhotoUrl,
                           }
                       };
        return messages;
    }
}
